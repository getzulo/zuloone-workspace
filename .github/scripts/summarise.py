#!/usr/bin/env python3
"""
Render the build as a GitHub step summary — what shipped, and at what version.

    summarise.py bundles/index.json >> "$GITHUB_STEP_SUMMARY"

Written to stdout rather than appending directly so it composes, and so running
it by hand shows the same thing the run page will.
"""

import json
import sys


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else "bundles/index.json"
    try:
        with open(path, encoding="utf-8") as f:
            index = json.load(f)
    except FileNotFoundError:
        # The build failed before producing anything. The error is already in the
        # log; a summary claiming nothing would only add noise.
        return

    package = index.get("package") or {}
    models = index.get("models") or []

    print("### Business layer")
    print()
    print("| | |")
    print("|---|---|")
    print(f"| version | `{package.get('version', '?')}` |")
    print(f"| release | `{package.get('release', False)}` |")
    print(f"| built against | `{package.get('platformImage', '?')}` |")
    print(f"| commit | `{(package.get('commit') or '')[:7]}` |")
    print(f"| models | {len(models)} |")

    if models:
        print()
        print("| model | version | layer | size |")
        print("|---|---|---|---|")
        for m in models:
            print(f"| {m['model']} | `{m['version']}` | {m.get('layerId', '?')} | {m['bytes'] // 1024} KB |")


if __name__ == "__main__":
    main()
