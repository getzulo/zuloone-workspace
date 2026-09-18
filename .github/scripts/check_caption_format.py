#!/usr/bin/env python3
"""Captions are language-keyed objects, not sibling keys.

    "caption": { "en": "Customer", "ru": "Клиент" }     <- current
    "caption": "Customer", "caption_ru": "Клиент"       <- previous

The importer still READS the old shape, so a stale file breaks nothing at run
time — which is exactly why it needs a gate. It just sits there in a format the
exporter no longer writes, and the next export rewrites it, producing a diff
nobody asked for. Uniformity is the whole point.

Two things are checked:

  1. no `<prop>_<lang>` siblings anywhere;
  2. every language-keyed caption carries "en" — it is the BASE, the object's
     own Caption column and the fallback every other language falls back to.
     A caption object without it puts a non-English value in that column.

Data-package index rows (`DataPackages/index.json`) legitimately carry
`caption_ru` / `caption_ar`: that is a separate catalogue format with its own
DTO, not an object envelope, and is skipped.

Usage: check_caption_format.py [paths...]   (default: whole tree)
Exit 1 on any violation.
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

# ("caption", "subtypeCaption") — mirrors WorkspaceInlineCaptions.Properties.
PROPS = ("caption", "subtypeCaption")
SIBLING = re.compile(r"^(%s)_([A-Za-z]{2,10})$" % "|".join(PROPS))
BASE_LANGUAGE = "en"

SKIP_DIRS = {".git", ".generated", "node_modules", "bin", "obj"}
# Not object envelopes: a pack catalogue with its own flat caption columns.
SKIP_PARTS = ("DataPackages",)


def walk(node, on_object):
    if isinstance(node, dict):
        on_object(node)
        for value in node.values():
            walk(value, on_object)
    elif isinstance(node, list):
        for item in node:
            walk(item, on_object)


def check(path):
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError):
        return []  # not ours to judge; other gates parse these too

    problems = []

    def inspect(obj):
        for key, value in obj.items():
            match = SIBLING.match(key)
            if match:
                problems.append(f'{key}: "{value}" — fold into "{match.group(1)}"')
            elif key in PROPS and isinstance(value, dict):
                if BASE_LANGUAGE not in value:
                    langs = ", ".join(sorted(value)) or "(empty)"
                    problems.append(f'{key} has no "{BASE_LANGUAGE}" base (only: {langs})')

    walk(data, inspect)
    return problems


def main(argv):
    targets = [Path(a) for a in argv[1:]] or [ROOT]
    files = []
    for target in targets:
        if target.is_file():
            files.append(target)
            continue
        for path in target.rglob("*.json"):
            if SKIP_DIRS & set(path.parts) or any(p in path.parts for p in SKIP_PARTS):
                continue
            files.append(path)

    failed = 0
    for path in sorted(files):
        problems = check(path)
        if not problems:
            continue
        failed += 1
        rel = path.relative_to(ROOT) if path.is_relative_to(ROOT) else path
        print(f"{rel}")
        for problem in problems[:10]:
            print(f"    {problem}")
        if len(problems) > 10:
            print(f"    … and {len(problems) - 10} more")

    if failed:
        print(
            f"\n{failed} file(s) carry captions in the previous format.\n"
            'Captions are language-keyed: "caption": { "en": …, "ru": … }.\n'
            "See docs/WORKSPACE.md §1.1 — an export of the stand rewrites them for you.",
            file=sys.stderr,
        )
        return 1

    print(f"caption format ok ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
