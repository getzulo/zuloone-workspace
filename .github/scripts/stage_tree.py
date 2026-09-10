#!/usr/bin/env python3
"""
Stage the model tree that ships inside the distribution image.

The tree — not the bundles — is what a tenant installs from. A bundle
(`ModelBundle` in ModelBundleService.cs) carries seven kinds of object: enums,
EDTs, dictionaries, registers, document types, standalone scripts and a menu
chunk. The workspace tree carries seventeen. Everything in the gap was being
dropped in silence: number sequences, form layouts, print forms, widgets and
their placements, total drivers, link tables, analytics, global constants.

That gap shipped. Common's ten dictionaries arrived at a customer without any of
its fourteen number sequences, so nothing could generate a code. Core was worse:
a bundle of it cannot even be produced (ExportAsync throws on a system model),
which is why forty-nine form layouts, fifteen number sequences, five print forms,
five total drivers and three widgets reached no tenant at all.

Prints the label value for `one.zulo.models` on stdout so the image can say what
it carries without being booted.
"""
import json
import os
import shutil
import sys

# Directories that are never part of a model, mirroring the skip list in
# WorkspaceImportService.ImportAllAsync — copying them would only make the image
# bigger, and .generated in particular is build output.
JUNK = {".generated", ".git", "node_modules", ".vscode", "bin", "obj"}


def main() -> int:
    out = sys.argv[1] if len(sys.argv) > 1 else "dist-workspace"
    cfg = json.load(open(".zuloone-release.json", encoding="utf-8"))
    skip = set(cfg.get("treeExclude") or [])

    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    staged, labels = [], []
    for name in sorted(os.listdir(".")):
        manifest = os.path.join(name, "model.json")
        if not os.path.isfile(manifest) or name in skip:
            continue
        try:
            obj = json.load(open(manifest, encoding="utf-8"))["object"]
        except Exception as ex:  # a model.json that will not parse is a build error
            print(f"::error::{manifest} could not be read: {ex}", file=sys.stderr)
            return 1

        shutil.copytree(
            name,
            os.path.join(out, name),
            ignore=shutil.ignore_patterns(*JUNK),
        )
        version = obj.get("modelVersion") or "0.0.0"
        staged.append({"model": obj["name"], "version": version, "dir": name})
        labels.append(f"{obj['name']}={version}")

    if not staged:
        print("::error::No model directories were staged — every one excluded?", file=sys.stderr)
        return 1

    with open(os.path.join(out, "index.json"), "w", encoding="utf-8") as f:
        json.dump({"models": staged}, f, indent=2)

    for m in staged:
        print(f"  staged {m['model']} {m['version']}", file=sys.stderr)
    print(",".join(labels))
    return 0


if __name__ == "__main__":
    sys.exit(main())
