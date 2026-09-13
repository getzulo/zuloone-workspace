#!/usr/bin/env python3
"""Refuse a tree where gitignore (or a partial add) split a script pair.

A MetaJob / command / event envelope without its .cs is a dangling FK on
import. That already happened: NewJobTask_1788463549156 was ignored, the Job
was committed, every stand went red.

Run from the workspace root:

    python3 .github/scripts/check_script_pairs.py
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


def git(*args: str) -> str:
    # Paths are UTF-8; Windows' default console codec is not.
    raw = subprocess.run(["git", *args], capture_output=True, check=True).stdout
    return raw.decode("utf-8", errors="surrogateescape")


def tracked() -> set[str]:
    return {p.replace("\\", "/") for p in git("ls-files", "-z").split("\0") if p}


def ignored() -> set[str]:
    raw = subprocess.run(
        ["git", "ls-files", "-i", "-o", "--exclude-standard", "-z"],
        capture_output=True,
        check=True,
    ).stdout.decode("utf-8", errors="surrogateescape")
    return {p.replace("\\", "/") for p in raw.split("\0") if p}


def script_meta_id(path: Path) -> str | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    obj = data.get("object") if isinstance(data, dict) else None
    if not isinstance(obj, dict):
        return None
    meta = obj.get("metaId")
    return meta if isinstance(meta, str) and meta else None


def subtype_tx_script_ids(path: Path) -> list[str]:
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return []
    if not isinstance(data, dict):
        return []
    buckets: list[object] = []
    raw = data.get("subtypeTransactionScripts")
    if isinstance(raw, list):
        buckets.append(raw)
    obj = data.get("object")
    if isinstance(obj, dict):
        nested = obj.get("subtypeTransactionScripts")
        if isinstance(nested, list):
            buckets.append(nested)
    ids: list[str] = []
    for rows in buckets:
        if not isinstance(rows, list):
            continue
        for row in rows:
            if not isinstance(row, dict):
                continue
            meta = row.get("scriptMetaId")
            if isinstance(meta, str) and meta:
                ids.append(meta)
    return ids


def job_script_meta_id(path: Path) -> str | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    if not isinstance(data, dict) or data.get("kind") != "Job":
        return None
    obj = data.get("object")
    if not isinstance(obj, dict):
        return None
    meta = obj.get("scriptMetaId")
    return meta if isinstance(meta, str) and meta else None


def main() -> int:
    files = tracked()
    hidden = ignored()
    root = Path(git("rev-parse", "--show-toplevel").strip())
    errors: list[str] = []

    scripts_by_id: dict[str, str] = {}
    for rel in sorted(files):
        if not rel.endswith(".script.json"):
            continue
        meta = script_meta_id(root / rel)
        if meta:
            scripts_by_id[meta] = rel
        cs = rel[: -len(".script.json")] + ".cs"
        on_disk = (root / cs).is_file()
        if on_disk and cs not in files:
            why = "ignored by .gitignore" if cs in hidden else "not tracked"
            errors.append(f"{rel} is tracked, sibling {cs} is {why}")

    for rel in sorted(files):
        if "/Jobs/" not in rel or rel.endswith(".script.json"):
            continue
        script_id = job_script_meta_id(root / rel)
        if not script_id:
            continue
        if script_id not in scripts_by_id:
            errors.append(
                f"{rel} scriptMetaId {script_id} has no tracked .script.json"
            )

    for rel in sorted(files):
        if not rel.endswith(".object.json"):
            continue
        for script_id in subtype_tx_script_ids(root / rel):
            if script_id not in scripts_by_id:
                errors.append(
                    f"{rel} subtypeTransactionScripts scriptMetaId {script_id} has no tracked .script.json"
                )

    skip_prefixes = (".generated/",)
    skip_suffixes = (".pyc",)
    for rel in sorted(hidden):
        if rel.startswith(skip_prefixes) or rel.endswith(skip_suffixes):
            continue
        if "/bin/" in rel or "/obj/" in rel:
            continue
        if not (rel.endswith(".script.json") or rel.endswith(".cs")):
            continue
        name = Path(rel).name
        if "-" in name and len(name.split("-")[-1].split(".")[0]) == 8:
            continue
        parent = str(Path(rel).parent).replace("\\", "/")
        prefix = parent + "/"
        if any(t.startswith(prefix) for t in files):
            errors.append(f"{rel} is ignored next to tracked files in {parent}/")

    if errors:
        print("Script pairs are split — this is how a Job lands without its code:")
        for line in errors:
            print(f"  {line}")
        return 1
    print("Script pairs: every tracked envelope has its code; jobs and subtype tx bindings resolve.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
