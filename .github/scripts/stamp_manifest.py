#!/usr/bin/env python3
"""
Record HOW a set of bundles was built, alongside WHAT is in it.

    stamp_manifest.py bundles/index.json

The package version, the commit and the platform image the bundles were exported
by. That last one matters: a bundle is produced by importing the workspace into a
running ZuloOne and exporting each model back out, so the platform version is
part of how the artifact was made — and once a model declares a MinVersion on
Core, it is what that constraint was tested against.
"""

import json
import os
import sys


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else "bundles/index.json"
    with open(path, encoding="utf-8") as f:
        index = json.load(f)

    index["package"] = {
        "version": os.environ.get("PKG_VERSION", "0.0.0"),
        # A string from the environment, compared explicitly — "false" is truthy.
        "release": os.environ.get("PKG_RELEASE", "false") == "true",
        "commit": os.environ.get("GITHUB_SHA", ""),
        "platformImage": os.environ.get("PKG_IMAGE", ""),
        "builtAt": os.environ.get("GITHUB_RUN_ID", ""),
    }

    with open(path, "w", encoding="utf-8") as f:
        json.dump(index, f, indent=2)
    print(json.dumps(index["package"], indent=2))


if __name__ == "__main__":
    main()
