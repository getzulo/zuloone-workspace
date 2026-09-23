import json, subprocess, sys

# Точечная доставка своих файлов на стенд под чужим гейтом (zuloone-verify §apply-file).
# Пара «envelope + .cs» неразделима: сервер сравнивает ОБЁРТКУ, поэтому .cs
# в одиночку не доезжает — клади .script.json рядом.
base = "http://localhost:5257/api/dev/workspace/apply-file"
bad = 0
for f in sys.argv[1:]:
    body = json.dumps({"file": f.replace("\\", "/"), "force": True})
    raw = subprocess.run(["curl", "-s", "-X", "POST", base,
                          "-H", "Content-Type: application/json", "-d", body],
                         capture_output=True, text=True, encoding="utf-8").stdout
    try:
        d = json.loads(raw)
    except Exception:
        print(f"{f}: НЕ JSON -> {raw[:300]}")
        bad += 1
        continue
    errs = d.get("errors") or []
    warns = d.get("warnings") or []
    print(f'{f}: created={d.get("created")} updated={d.get("updated")} '
          f'unchanged={d.get("unchanged")} coreRejected={d.get("coreRejected")}')
    for e in errs:
        print("   ERR:", str(e)[:400]); bad += 1
    for w in warns:
        print("   WARN:", str(w)[:400])
sys.exit(1 if bad else 0)
