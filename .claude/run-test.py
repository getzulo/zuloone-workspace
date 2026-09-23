import json, subprocess, sys

# Прогон тестов по GUID через API стенда с человекочитаемым итогом.
# Ответ /run: {"scripts":[{"testName","cases":[{"name","outcome","errorDetail"}]}]}
# Консоль Windows по умолчанию cp1252 — кириллица в сообщениях падала бы
# UnicodeEncodeError уже ПОСЛЕ прогона, пряча настоящую причину отказа.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
base = "http://localhost:5257/api/metadata/tests"
bad = 0
for tid in sys.argv[1:]:
    raw = subprocess.run(["curl", "-s", "-X", "POST", f"{base}/{tid}/run"],
                         capture_output=True, text=True, encoding="utf-8").stdout
    try:
        d = json.loads(raw)
    except Exception:
        print(f"{tid}: НЕ JSON -> {raw[:400]}")
        bad += 1
        continue
    for s in d.get("scripts", []):
        cs = s.get("cases", [])
        p = sum(1 for c in cs if c.get("outcome") == "Passed")
        print(f'{s.get("testName")}: {p}/{len(cs)}')
        if s.get("compileError"):
            print("  COMPILE:", s["compileError"][:900])
            bad += 1
        for c in cs:
            if c.get("outcome") != "Passed":
                bad += 1
                msg = c.get("errorDetail") or c.get("output") or ""
                print("  FAIL:", c.get("name"), "|", msg[:900])
sys.exit(1 if bad else 0)
