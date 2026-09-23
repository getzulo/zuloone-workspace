#!/bin/sh
# Прогон тестов по GUID через API стенда с человекочитаемым итогом.
# Ответ /run: {"scripts":[{"testName","cases":[{"name","outcome","errorDetail"}]}]}
for id in "$@"; do
  curl -s -X POST "http://localhost:5257/api/metadata/tests/$id/run" | python -c '
import json,sys
d=json.load(sys.stdin)
for s in d.get("scripts",[]):
    cs=s.get("cases",[])
    p=sum(1 for c in cs if c.get("outcome")=="Passed")
    print(f"{s.get(\"testName\")}: {p}/{len(cs)}")
    if s.get("compileError"): print("  COMPILE:", s["compileError"][:900])
    for c in cs:
        if c.get("outcome")!="Passed":
            print("  FAIL:", c.get("name"), "|", (c.get("errorDetail") or c.get("output") or "")[:900])
'
done
