#!/usr/bin/env python3
"""Refuse a tree where one model reaches into another without declaring it.

The layering rule is "a model may only see downwards": Sales may use Inventory,
Inventory must never know Sales exists. Drop Production from a tenant and the
rest must still start. That rule was believed to be enforced by the compiler.
It is not, and the tree proved it: Accounting — layer 1, depended on by
GLIntegration, HR, Inventory, Purchasing, Sales and Tax, i.e. everything — had
grown a field extension on Production.BillOfMaterials and a menu item hung
inside Production's own menu group. Neither direction was declared. Shipping a
tenant without Production would have taken Accounting, and therefore the whole
stack, with it. Both were designer experiments ("Test", a caption with no
translation) that nothing would ever have flagged.

WHAT IS CHECKED. Every "...MetaId" reference in every model's json is resolved
to the model that owns the target, and one of these must hold:

  * same model                       — trivially fine;
  * target ∈ declared deps of source — the normal direction, A depends on B;
  * source ∈ declared deps of target — the EXTENSION direction. When Costing
    hangs a totals driver on Inventory's Stock, the platform writes the pointer
    into Inventory/Registers/Stock/Stock.object.json, so the file reference runs
    Inventory → Costing while the dependency runs Costing → Inventory. The
    pointer is backwards, the dependency is not, and the model is still
    droppable. This is by design, so it passes.

Anything else is a model reaching sideways into a stranger, and fails.

STRING NAMES. Registers and global constants are also addressed from .cs by
literal ("RegisterMovementSpec(\"Stock\")", GlobalConstants.Get("AmountScale")).
Those edges have no MetaId, so the JSON walk cannot see them. After the object
index is built, this script scans .cs outside Tests/ for a closed list of call
shapes (the same list as platform ScriptNameReferences) and requires the named
object's owner to be the calling model or in its dep closure. Tests/ may read
a downstream register (Sales asserting VatPayable); that is not a shippable
edge. Dictionary names, .Dim / .Res, and unknown strings are ignored — that is
what drowned an earlier unscoped scan.

Run from the workspace root:

    python3 .github/scripts/check_model_deps.py
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

SKIP_DIRS = {".generated", "bin", "obj", ".git", "host", ".github", ".claude", "_unassigned"}

# Keys that carry an object reference. "metaId" is identity, not a reference,
# and the model-dependency bookkeeping keys point at models by definition.
IGNORED_KEYS = {"metaId", "modelId", "modelMetaId", "dependsOnModelMetaId"}

PLACEHOLDER_MODEL = "00000000-0000-0000-0000-000000000000"

# Closed list, same shapes as zulo.one ScriptNameReferences. .Dim / .Res stay out.
ADDRESSING = re.compile(
    r"(?:RegisterMovementSpec|PostMovementAsync|GetBalanceAsync|QueryBalancesAsync|"
    r"QueryMovementsAsync|SetInformationAsync|SliceLastAsync|SliceFirstAsync|"
    r"QueryInformationAsync|DeleteInformationAsync|GetInformationAsync|"
    r"CompileVirtualTotalAsync|QueryVirtualTotalAsync)\s*\(\s*\"(?P<lit>[^\"]+)\""
    r"|GlobalConstants\s*\.\s*(?:Get|GetAsync|SetAsync)\s*(?:<[^>]+>)?\s*\(\s*\"(?P<const>[^\"]+)\""
    r"|(?:SetAsync|SliceLastAsync|SliceFirstAsync|SetInformationAsync|"
    r"GetInformationAsync|QueryInformationAsync)\s*<\s*(?P<typed>[A-Za-z_]\w*)"
    r"|SetAsync(?:<[^>]+>)?\s*\(\s*new\s+(?P<ctor>[A-Za-z_]\w*)"
)

NAMED_KINDS = {"Register", "GlobalConstant"}


def names_in(code: str) -> set[str]:
    found: set[str] = set()
    for match in ADDRESSING.finditer(code):
        for group in match.groupdict().values():
            if group:
                found.add(group)
    return found


def cs_files(folder: Path):
    for path in sorted(folder.rglob("*.cs")):
        if any(part in SKIP_DIRS or part == "Tests" for part in path.parts):
            continue
        yield path


def load(path: Path) -> object | None:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        print(f"  ! unreadable {path}: {exc}", file=sys.stderr)
        return None


def model_folders(root: Path) -> dict[str, Path]:
    return {p.name: p for p in sorted(root.iterdir()) if (p / "model.json").is_file()}


def objects_in(node: object):
    """Yield every dict that looks like a metadata object."""
    if isinstance(node, dict):
        obj = node.get("object")
        if isinstance(obj, dict):
            yield obj
        for key, value in node.items():
            if key != "object" and isinstance(value, (dict, list)):
                yield from objects_in(value)
    elif isinstance(node, list):
        for item in node:
            yield from objects_in(item)


def refs_in(node: object):
    """Yield (key, target_meta_id) for every reference-shaped field."""
    if isinstance(node, dict):
        for key, value in node.items():
            if key in IGNORED_KEYS:
                continue
            if isinstance(value, str) and key.endswith("MetaId"):
                yield key, value
            elif isinstance(value, (dict, list)):
                yield from refs_in(value)
    elif isinstance(node, list):
        for item in node:
            yield from refs_in(item)


def json_files(folder: Path):
    for path in sorted(folder.rglob("*.json")):
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        yield path


def main() -> int:
    root = Path.cwd()
    folders = model_folders(root)
    if not folders:
        print("No model.json found — run this from the workspace root.")
        return 1

    models: dict[str, str] = {}          # model metaId -> name
    folder_model: dict[str, str] = {}    # folder name  -> model metaId
    declared: dict[str, set[str]] = {}   # model metaId -> directly declared deps

    for name, folder in folders.items():
        doc = load(folder / "model.json")
        if not isinstance(doc, dict):
            return 1
        obj = doc["object"]
        meta_id = obj["metaId"]
        models[meta_id] = obj["name"]
        folder_model[name] = meta_id
        declared[meta_id] = {
            d["dependsOnModelMetaId"] for d in (doc.get("dependencies") or [])
        }

    def closure(meta_id: str) -> set[str]:
        seen: set[str] = set()
        stack = list(declared.get(meta_id, ()))
        while stack:
            current = stack.pop()
            if current in seen:
                continue
            seen.add(current)
            stack.extend(declared.get(current, ()))
        return seen

    reach = {m: closure(m) for m in models}

    cycles = sorted(models[m] for m in models if m in reach[m])
    if cycles:
        print("Dependency cycle — a model depends on itself through the graph:")
        for name in cycles:
            print(f"  {name}")
        return 1

    # Index every object so a reference can be resolved to its owning model.
    owner: dict[str, str] = {}
    described: dict[str, tuple[str, str]] = {}
    for folder_name, folder in folders.items():
        home = folder_model[folder_name]
        for path in json_files(folder):
            if path.name == "model.json":
                continue
            doc = load(path)
            if doc is None:
                continue
            for obj in objects_in(doc):
                meta_id = obj.get("metaId")
                if not isinstance(meta_id, str):
                    continue
                model_id = obj.get("modelId")
                owner[meta_id] = home if (not model_id or model_id == PLACEHOLDER_MODEL) else model_id
                described[meta_id] = (
                    obj.get("name") or obj.get("fieldName") or "?",
                    str(path.relative_to(root)).replace("\\", "/"),
                )

    violations: list[str] = []
    extension_edges = 0
    for folder_name, folder in folders.items():
        home = folder_model[folder_name]
        for path in json_files(folder):
            if path.name == "model.json":
                continue
            doc = load(path)
            if doc is None:
                continue
            rel = str(path.relative_to(root)).replace("\\", "/")
            for key, target in refs_in(doc):
                target_owner = owner.get(target)
                if target_owner is None or target_owner == home:
                    continue
                if target_owner in reach[home]:
                    continue
                if home in reach.get(target_owner, set()):
                    extension_edges += 1        # the extension direction, by design
                    continue
                name, _ = described.get(target, ("?", ""))
                violations.append(
                    f"{models[home]} -> {models.get(target_owner, target_owner)}"
                    f"  [{key} = {name}]  in {rel}"
                )

    if violations:
        print("A model reaches into another it does not depend on —")
        print("drop the target from a tenant and the source breaks with it:")
        for line in sorted(set(violations)):
            print(f"  {line}")
        print()
        print("Declare the dependency in model.json, or move the object to the model that owns it.")
        return 1

    print(
        f"Model dependencies: {len(models)} models, no cycles, "
        f"every cross-model reference declared "
        f"({extension_edges} extension-direction pointers accepted). "
        "Any model with no dependents can be left out of a tenant."
    )

    named_owner: dict[str, tuple[str, str]] = {}  # name -> (modelId, kind)
    for folder_name, folder in folders.items():
        home = folder_model[folder_name]
        for path in json_files(folder):
            doc = load(path)
            if not isinstance(doc, dict):
                continue
            kind = doc.get("kind")
            if kind not in NAMED_KINDS:
                continue
            obj = doc.get("object")
            if not isinstance(obj, dict):
                continue
            name = obj.get("name")
            if not isinstance(name, str) or not name:
                continue
            model_id = obj.get("modelId")
            owner_id = home if (not model_id or model_id == PLACEHOLDER_MODEL) else model_id
            named_owner[name] = (owner_id, kind)

    named_owner_ci = {key.lower(): value for key, value in named_owner.items()}

    string_hits = 0
    string_violations: list[str] = []
    for folder_name, folder in folders.items():
        home = folder_model[folder_name]
        for path in cs_files(folder):
            try:
                code = path.read_text(encoding="utf-8-sig")
            except OSError as exc:
                print(f"  ! unreadable {path}: {exc}", file=sys.stderr)
                continue
            rel = str(path.relative_to(root)).replace("\\", "/")
            for name in names_in(code):
                owned = named_owner.get(name) or named_owner_ci.get(name.lower())
                if owned is None:
                    continue
                owner_id, kind = owned
                string_hits += 1
                if owner_id == home or owner_id in reach[home]:
                    continue
                string_violations.append(
                    f"{models[home]} -> {models.get(owner_id, owner_id)}"
                    f"  [{kind} {name}]  in {rel}"
                )

    if string_violations:
        print()
        print("A script names a register or constant of a model it does not depend on —")
        print("the JSON gate cannot see these string edges:")
        for line in sorted(set(string_violations)):
            print(f"  {line}")
        print()
        print("Declare the dependency in model.json, or move the script to the model that owns the object.")
        return 1

    print(
        f"String names: {string_hits} register/constant addresses, "
        f"{len(named_owner)} named objects, all inside declared closures."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
