#!/bin/bash
# Does an UPGRADE actually apply?
#
# The build's own import cannot tell. It runs against an empty database, and on an
# empty database a create-only import and an updating one are indistinguishable.
# That blind spot shipped: a model keeps its MetaIds at every version — Sales 1.1.0
# and 1.2.0 share every one of them, down to the dictionaries inside — so installing
# a newer version over an older one passed over every changed row, reported success,
# and let the package row record a number the tenant did not run.
#
# So this stands up a stand on the PREVIOUS release's tree, then boots the same
# stand on this build's tree over the same database, and looks at what moved.
#
# Arguments: <platform-image> <previous-git-ref>
set -Eeuo pipefail
trap 'echo "::error::Upgrade gate failed at line ${LINENO}: ${BASH_COMMAND}" >&2' ERR

IMG="${1:?platform image required}"
PREV="${2:?previous git ref required}"
PG="ug-pg-$$"
APP="ug-app-$$"
NET="ug-net-$$"

cleanup() {
  local rc=$?
  if [ "$rc" -ne 0 ]; then
    # Installation can report an error long before /health becomes ready.
    # Keep those diagnostics visible even when a later assertion fails.
    docker logs "$APP" 2>&1 | python3 -c '
import json, sys
for line in sys.stdin:
    try:
        row = json.loads(line)
    except ValueError:
        continue
    message = row.get("@mt", "").lower()
    if row.get("@l") in ("Error", "Fatal") or any(part in message for part in (
        "model tree", "package versions", "import warnings"
    )):
        detail = {k: v for k, v in row.items() if k in (
            "@t", "@mt", "@l", "Errors", "Held", "Names", "Count",
            "Created", "Updated", "Skipped"
        )}
        if "@x" in row:
            detail["exception"] = row["@x"][:2000]
        print(json.dumps(detail, ensure_ascii=False))
' >&2 || true
  fi
  docker rm -fv "$APP" "$PG" >/dev/null 2>&1 || true
  docker network rm "$NET" >/dev/null 2>&1 || true
  # The staged trees live inside the checkout, and the image build runs after this
  # step — leaving them would put two extra directories in front of stage_tree.py.
  rm -rf .ug-prev .ug-now
}
trap cleanup EXIT

# Two trees: the previous release, and this build. stage_tree.py already knows
# which models ship; for the previous ref we take the same directories out of git,
# which needs no checkout and cannot be confused by the working tree.
rm -rf .ug-prev .ug-now
python3 .github/scripts/stage_tree.py .ug-now >/dev/null
mkdir -p .ug-prev
for d in .ug-now/*/; do
  m=$(basename "$d")
  # A new folder (Local, a just-added model) is not at PREV. Do not pipe a
  # failed `git archive` into tar — empty stdin is "this does not look like
  # a tar archive", which reads as a broken gate rather than a skip.
  if ! git cat-file -e "$PREV:$m" 2>/dev/null; then
    echo "  $m did not exist at $PREV — it is new, so nothing to upgrade from."
    continue
  fi
  git archive "$PREV" -- "$m/" | tar -x -C .ug-prev
done

moved=$(python3 - "$PREV" <<'PY'
import json, subprocess, sys, os
prev = sys.argv[1]
out = []
for m in sorted(os.listdir(".ug-now")):
    now_file = os.path.join(".ug-now", m, "model.json")
    if not os.path.isfile(now_file):
        continue
    now = json.load(open(now_file, encoding="utf-8"))["object"].get("modelVersion")
    raw = subprocess.run(["git", "show", f"{prev}:{m}/model.json"], capture_output=True)
    was = None
    if raw.returncode == 0:
        try:
            was = json.loads(raw.stdout.decode("utf-8"))["object"].get("modelVersion")
        except Exception:
            pass
    if was is not None and was != now:
        out.append(f"{m} {was}->{now}")
print(",".join(out))
PY
)

docker network create "$NET" >/dev/null
docker run -d --name "$PG" --network "$NET" \
  -e POSTGRES_USER=ug -e POSTGRES_PASSWORD=ug -e POSTGRES_DB=ug postgres:17-alpine >/dev/null
for _ in $(seq 1 30); do docker exec "$PG" pg_isready -U ug -q && break; sleep 2; done
docker exec "$PG" pg_isready -U ug -q

# A free port PER BOOT, not one for the whole run. The first stand's publisher
# can still hold its port for a moment after the container is removed, and a
# `docker run` that loses that race dies under `set -e` with its message on a
# stderr nobody reads — which is how this failed in CI while passing by hand.
# Re-probing costs nothing and removes the race; reporting the failure costs a
# line and removes the guesswork.
#
# CoreDevelopmentMode matches the compile stand. The pin (2026.0.120) installs
# the tree under PlatformInstallScope, then backfills ID fields OUTSIDE it —
# on a tenant that write is "Sales is a Zulo product model and is read-only"
# and the process dies before /health. The first successful gate ran on an
# older image without that lock. This flag is the CI workaround until the
# platform wraps that backfill in the same scope (real tenants need that).
boot() {
  docker rm -fv "$APP" >/dev/null 2>&1 || true
  PORT=$(python3 -c "import socket;s=socket.socket();s.bind(('127.0.0.1',0));print(s.getsockname()[1]);s.close()")
  if ! docker run -d --name "$APP" --network "$NET" -p "127.0.0.1:${PORT}:8080" \
    -v "$PWD/$1":/opt/zuloone/workspace:ro \
    -e Database__Provider=PostgreSql \
    -e "ConnectionStrings__DefaultConnection=Host=${PG};Database=ug;Username=ug;Password=ug" \
    -e Jwt__SigningKey=upgrade-gate-not-a-secret-0123456789abcdef \
    -e ASPNETCORE_URLS=http://+:8080 \
    -e ZuloOne__CoreDevelopmentMode=true \
    -e 'ZuloOne__Packages__Install=*' \
    -e Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning \
    "$IMG" >/dev/null
  then
    echo "::error::Could not start the stand on $1 (port ${PORT})."
    return 1
  fi
  for _ in $(seq 1 480); do
    if [ -z "$(docker ps -q --filter "name=^/${APP}$")" ]; then
      echo "::error::The stand on $1 exited before ready."
      docker logs --tail 60 "$APP" 2>&1 | grep -v '"SourceContext":"Microsoft.EntityFrameworkCore.Database.Command"' | tail -25
      return 1
    fi
    body=$(curl -s -m 2 "http://127.0.0.1:${PORT}/health" 2>/dev/null || true)
    case "${body// /}" in *'"ready":true'*) return 0;; esac
    sleep 1
  done
  echo "::error::The stand on $1 never reported ready."
  docker logs --tail 60 "$APP" 2>&1 | grep -v '"SourceContext":"Microsoft.EntityFrameworkCore.Database.Command"' | tail -25
  return 1
}

summary() {
  local logs counters
  logs=$(docker logs "$APP" 2>&1) || return 1
  if counters=$(printf '%s\n' "$logs" | grep -o '"Created":[0-9]*,"Updated":[0-9]*,"Skipped":[0-9]*' | tail -1); then
    printf '%s\n' "$counters"
  elif [[ "$logs" == *"Model tree is current:"* ]]; then
    # A repeat boot with no changed versions does not run the importer, so
    # there is no Created/Updated event. It is a valid zero-update install.
    printf '%s\n' '"Created":0,"Updated":0,"Skipped":0'
  else
    echo "::error::The stand is ready but has no model-tree installation result." >&2
    printf '%s\n' "$logs" | tail -40 >&2
    return 1
  fi
}

echo "Previous release: $PREV"
echo "Model versions that moved since then: ${moved:-none}"

# errexit OFF around the boots. With it on, anything failing inside boot() ends
# the script at that instant with no output at all — which is what happened
# twice: the log jumped straight from the phase heading to "exit code 1" and
# said nothing about why. A gate that fails silently teaches people to ignore it.
run_boot() {
  set +e
  boot "$1"
  local rc=$?
  set -e
  if [ "$rc" -ne 0 ]; then
    echo "::error::The stand on $1 did not come up (rc=${rc})."
    docker ps -a --filter "name=${APP}" --format "  container: {{.Status}}" || true
    exit 1
  fi
}

echo "--- installing the previous tree ---"
run_boot .ug-prev
previous_summary=$(summary)
echo "  ${previous_summary}"

echo "--- installing this build's tree over it ---"
run_boot .ug-now
now_summary=$(summary)
echo "  ${now_summary}"

# A positive aggregate Updated count can hide one failed or stale model.
# Check both metadata and package stamps after startup compilation completed.
python3 .github/scripts/check_installed_models.py "$PG" .ug-now

updated=$(echo "$now_summary" | sed -n 's/.*"Updated":\([0-9]*\).*/\1/p')
updated=${updated:-0}

if [ -n "$moved" ] && [ "$updated" -eq 0 ]; then
  echo "::error::${moved} — but the second install updated 0 rows."
  echo "::error::A model keeps its MetaIds across versions, so an install that only"
  echo "::error::CREATES reports success while changing nothing, and the package row"
  echo "::error::then claims a version the tenant does not run."
  exit 1
fi

if [ -z "$moved" ]; then
  echo "No model version moved since ${PREV}, so there is no upgrade to assert on."
  echo "The path was still exercised: both installs booted and the second reported ${now_summary}."
else
  echo "Upgrade applied: ${updated} row(s) rewritten for ${moved}."
fi
