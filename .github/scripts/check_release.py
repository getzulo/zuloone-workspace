#!/usr/bin/env python3
"""
Refuse a release that ships the same content under a new number.

    check_release.py <tag>

Compares every model.json's modelVersion against the previous tag. A release
where nothing moved teaches everyone to ignore version numbers, and the fleet
already has that problem: twelve of fourteen models still sit at 1.0.0 and all
forty-two dependency edges declare minVersion 1.0.0, so the gate that exists
currently constrains nothing.
"""

import json
import subprocess
import sys


def git(*args: str) -> str:
    return subprocess.run(["git", *args], capture_output=True, text=True).stdout.strip()


def version_at(ref: str, path: str) -> str | None:
    """modelVersion of one model.json at a git ref, or None if absent/unreadable."""
    raw = subprocess.run(["git", "show", f"{ref}:{path}"], capture_output=True)
    if raw.returncode != 0:
        return None
    try:
        return json.loads(raw.stdout.decode("utf-8"))["object"].get("modelVersion")
    except Exception:
        # A file that cannot be parsed is a problem, but not THIS check's problem
        # — the import in the build will say so far more precisely.
        return None


def main() -> None:
    tag = sys.argv[1] if len(sys.argv) > 1 else ""
    previous = git("describe", "--tags", "--abbrev=0", f"{tag}^") if tag else ""
    if not previous:
        print("First release — nothing to compare against.")
        return

    models = [p for p in git("ls-files", "*/model.json").splitlines() if p]
    moved = []
    for path in models:
        now = version_at("HEAD", path)
        was = version_at(previous, path)
        if now != was:
            moved.append(f"{path.rsplit('/', 1)[0]}: {was or 'absent'} -> {now or 'absent'}")

    for line in moved:
        print(f"  {line}")

    if not moved:
        print(f"::error::No model changed its modelVersion since {previous}.")
        print("::error::Bump the models you changed. A release nobody can tell apart from")
        print("::error::the last one makes every version number in the fleet meaningless.")
        sys.exit(1)

    print(f"{len(moved)} model version(s) moved since {previous}.")


if __name__ == "__main__":
    main()
