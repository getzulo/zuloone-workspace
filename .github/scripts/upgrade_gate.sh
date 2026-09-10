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
set -euo pipefail

IMG="${1:?platform image required}"
PREV="${2:?previous git ref required}"
PG="ug-pg-$$"
APP="ug-app-$$"
NET="ug-net-$$"
PORT=$(python3 -c "import socket;s=socket.socket();s.bind(('127.0.0.1',0));print(s.getsockname()[1]);s.close()")

cleanup() {
  docker rm -f "$APP" "$PG" >/dev/null 2>&1 || true
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
  git archive "$PREV" -- "$m/" 2>/dev/null | tar -x -C .ug-prev || {
    echo "  $m did not exist at $PREV — it is new, so nothing to upgrade from."
  }
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

boot() {
  docker rm -f "$APP" >/dev/null 2>&1 || true
  docker run -d --name "$APP" --network "$NET" -p "127.0.0.1:${PORT}:8080" \
    -v "$PWD/$1":/opt/zuloone/workspace:ro \
    -e Database__Provider=PostgreSql \
    -e "ConnectionStrings__DefaultConnection=Host=${PG};Database=ug;Username=ug;Password=ug" \
    -e Jwt__SigningKey=upgrade-gate-not-a-secret-0123456789abcdef \
    -e ASPNETCORE_URLS=http://+:8080 \
    -e 'ZuloOne__Packages__Install=*' \
    -e Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning \
    "$IMG" >/dev/null
  for _ in $(seq 1 150); do
    body=$(curl -s -m 5 "http://127.0.0.1:${PORT}/health" 2>/dev/null || true)
    case "${body// /}" in *'"ready":true'*) return 0;; esac
    sleep 4
  done
  echo "::error::The stand on $1 never reported ready."
  docker logs --tail 60 "$APP" 2>&1 | grep -v '"SourceContext":"Microsoft.EntityFrameworkCore.Database.Command"' | tail -25
  return 1
}

summary() { docker logs "$APP" 2>&1 | grep -o '"Created":[0-9]*,"Updated":[0-9]*,"Skipped":[0-9]*' | tail -1; }

echo "Previous release: $PREV"
echo "Model versions that moved since then: ${moved:-none}"

echo "--- installing the previous tree ---"
boot .ug-prev
echo "  $(summary)"

echo "--- installing this build's tree over it ---"
boot .ug-now
now_summary=$(summary)
echo "  ${now_summary}"

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
