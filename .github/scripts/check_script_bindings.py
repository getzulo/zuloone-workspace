#!/usr/bin/env python3
"""Refuse a tree where a script envelope names a target it no longer matches.

The runtime resolves EVENT HANDLERS by `objectName`. Transaction scripts resolve
through the subtype binding instead, i.e. by metaId. Renaming a document updates
the metaId wiring and leaves every envelope's `objectName` on the old string —
so the document goes on posting its stock and revenue movements while not one of
its handlers is ever invoked again.

That is exactly what happened when SalesInvoice became SalesRealization. Ten
envelopes kept the old name. Nothing failed, nothing logged: a handler that is
not wired cannot throw. The invoice posted, and then quietly skipped the legal
entity stamp, the tax rate, the TaxCalculation, the VAT accrual, the GL posting,
the loyalty points and the customer defaults — 30 failing cases in 14 test
classes, each describing a different symptom of this one line.

Command envelopes carry the same risk from the other side: `scriptType` must be
"DocumentCommand" with `objectType` "Command" and `objectMetaId` pointing at the
COMMAND, not at the document. Two commands had it backwards, which made the
platform emit a non-generic DocumentCommandBase and fail the override with
CS0115 — an error that reads like a bad signature and is really a bad binding.

Run from the workspace root:

    python3 .github/scripts/check_script_bindings.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

SKIP_DIRS = {".generated", "bin", "obj", ".git", "host", ".github", ".claude", "_unassigned"}

# scriptType -> the objectType it must be paired with. A command script hangs off
# the COMMAND row; everything else hangs off the object it extends.
COMMAND_KINDS = {
    "DocumentCommand": "Command",
    "DocumentListCommand": "Command",
    "DictionaryCommand": "Command",
    "DictionaryListCommand": "Command",
    "UserCommand": "Command",
}


def load(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        print(f"  ! unreadable {path}: {exc}", file=sys.stderr)
        return None


def walk(root: Path, suffix: str):
    for path in sorted(root.rglob(f"*{suffix}")):
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        yield path


def main() -> int:
    root = Path.cwd()
    if not any(root.glob("*/model.json")):
        print("No model.json found — run this from the workspace root.")
        return 1

    # metaId -> name, for everything a script can point at.
    names: dict[str, str] = {}
    for path in walk(root, ".json"):
        doc = load(path)
        if not isinstance(doc, dict):
            continue
        obj = doc.get("object")
        if isinstance(obj, dict) and isinstance(obj.get("metaId"), str) and obj.get("name"):
            names[obj["metaId"]] = obj["name"]

    stale: list[str] = []
    mispaired: list[str] = []
    for suffix in (".script.json", ".printform.json"):
        for path in walk(root, suffix):
            doc = load(path)
            if not isinstance(doc, dict):
                continue
            obj = doc.get("object") or {}
            rel = str(path.relative_to(root)).replace("\\", "/")

            target, named = obj.get("objectMetaId"), obj.get("objectName")
            if target and named:
                real = names.get(target)
                # A link table's metadata row is named PriceTypeHistory while its
                # runtime/generated identity is LT_PriceTypeHistory, and the
                # exporter writes the prefixed form. Accept either; it is a naming
                # convention, not the drift this gate is looking for.
                if obj.get("objectType") == "LinkTable" and named == f"LT_{real}":
                    real = named
                if real and real != named:
                    stale.append(f"{rel}\n      names {named!r}, but {target} is {real!r}")

            kind = obj.get("scriptType")
            want = COMMAND_KINDS.get(kind or "")
            if want and obj.get("objectType") != want:
                mispaired.append(
                    f"{rel}\n      scriptType {kind!r} needs objectType {want!r}, "
                    f"found {obj.get('objectType')!r}"
                )
            if want and not obj.get("objectMetaId"):
                mispaired.append(f"{rel}\n      scriptType {kind!r} has no objectMetaId")

    if stale or mispaired:
        if stale:
            print("A script envelope names a target that has been renamed —")
            print("handlers resolve by name, so these are wired to nothing:")
            for line in stale:
                print(f"  {line}")
        if mispaired:
            if stale:
                print()
            print("A command script is bound to the wrong kind of row:")
            for line in mispaired:
                print(f"  {line}")
        return 1

    print(
        "Script bindings: every envelope names the object its metaId points at, "
        "and every command script hangs off its command row."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
