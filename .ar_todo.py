#!/usr/bin/env python3
"""Arabic backlog + the glossary already in the tree.

Writes two files:
  _ar_todo.json  — distinct English captions with no ar, frequency-ordered,
                   carrying ru as a cross-check.
  _ar_glossary.json — en -> ar pairs that ALREADY exist, so the new
                   translations reuse the terminology instead of inventing a
                   second word for "Item".
"""

import json
import sys
from collections import Counter
from pathlib import Path

PROPS = ("caption", "subtypeCaption")
SKIP_DIRS = {".git", ".generated", "node_modules", "bin", "obj", ".claude"}
SKIP_MODELS = {"TestBench", "TestBenchExt", "Core"}

freq = Counter()
russian = {}
glossary = {}


def walk(node):
    if isinstance(node, dict):
        for prop in PROPS:
            value = node.get(prop)
            if isinstance(value, dict):
                base = value.get("en")
                if not base:
                    continue
                if value.get("ar"):
                    glossary[base] = value["ar"]
                else:
                    freq[base] += 1
                    if value.get("ru"):
                        russian[base] = value["ru"]
            elif isinstance(value, str) and value.strip():
                freq[value] += 1
        for v in node.values():
            walk(v)
    elif isinstance(node, list):
        for v in node:
            walk(v)


for path in sorted(Path(".").rglob("*.json")):
    if SKIP_DIRS & set(path.parts) or "DataPackages" in path.parts:
        continue
    if len(path.parts) < 2 or path.parts[0] in SKIP_MODELS:
        continue
    try:
        walk(json.loads(path.read_text(encoding="utf-8")))
    except Exception:
        continue

todo = [{"en": en, "n": n, "ru": russian.get(en, "")} for en, n in freq.most_common()]
Path("_ar_todo.json").write_text(
    json.dumps(todo, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
Path("_ar_glossary.json").write_text(
    json.dumps(glossary, ensure_ascii=False, indent=1, sort_keys=True) + "\n",
    encoding="utf-8")
print(f"distinct missing: {len(todo)}   occurrences: {sum(freq.values())}")
print(f"glossary (already translated): {len(glossary)}")
