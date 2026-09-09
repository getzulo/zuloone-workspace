#!/usr/bin/env python3
"""
Import this workspace into a running ZuloOne, compile it, and export one bundle
per shippable model.

Run against a THROWAWAY stand — it seeds the first administrator, which only
works on an empty database, and it turns on developer endpoints.

    build_bundles.py --base http://127.0.0.1:18080 --root . --out bundles

The gate is the middle of this script, not the end: an artifact exists only if
the whole tree imported, the schema materialised, and every model compiled. The
bundles are a by-product; the proof is the point.
"""

import argparse
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request


class Api:
    """Minimal HTTP client. No dependencies — this runs on a bare runner."""

    def __init__(self, base: str):
        self.base = base.rstrip("/")
        self.token: str | None = None

    def _call(self, method: str, path: str, body=None, timeout=600):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        if data is not None:
            req.add_header("Content-Type", "application/json")
        if self.token:
            req.add_header("Authorization", f"Bearer {self.token}")
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.status, r.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode("utf-8", "replace")

    def get(self, path, **kw):
        return self._call("GET", path, **kw)

    def post(self, path, body=None, **kw):
        return self._call("POST", path, body, **kw)


def fail(message: str, detail: str = "") -> None:
    """GitHub renders ::error:: in the log and on the summary."""
    print(f"::error::{message}")
    for line in (detail or "").splitlines()[:40]:
        print(f"::error::  {line}")
    sys.exit(1)


def wait_ready(api: Api, seconds: int) -> None:
    """
    Cold first boot runs migrations, DDL sync and a Roslyn compile before Kestrel
    binds, so this is minutes rather than seconds.
    """
    deadline = time.time() + seconds
    while time.time() < deadline:
        try:
            status, body = api.get("/health", timeout=10)
            if status == 200 and '"ready":true' in body.replace(" ", ""):
                print(f"platform ready: {body}")
                return
        except Exception:
            pass
        time.sleep(3)
    fail(f"The platform never reported ready within {seconds}s.")


def seed_admin(api: Api) -> None:
    """
    /api/auth/setup is anonymous and ONE-SHOT: it works precisely because this
    database has no users. Everything after this needs a bearer.
    """
    status, body = api.post("/api/auth/setup", {
        "name": "ci", "email": "ci@example.com",
        "password": "workspace-ci-not-a-secret",
    })
    if status != 200:
        fail(f"Could not seed an administrator ({status}).", body)
    token = json.loads(body).get("token")
    if not token:
        fail("The setup call returned no token.", body)
    api.token = token


def import_workspace(api: Api, path: str) -> None:
    status, body = api.post("/api/dev/workspace/import-all", {"path": path})
    if status == 404:
        fail("/api/dev/workspace/import-all answered 404 — the stand is not in "
             "CoreDevelopmentMode, so the developer endpoints do not exist.")
    if status != 200:
        fail(f"import-all failed ({status}).", body)

    result = json.loads(body)
    print(json.dumps({k: v for k, v in result.items() if k != "warnings"}, indent=2)[:2000])
    errors = result.get("errors") or []
    if errors:
        fail(f"import-all reported {len(errors)} error(s) — the workspace does "
             f"not import cleanly.", "\n".join(map(str, errors)))
    # Core rows are rejected by design (Core syncs down only), so a non-zero
    # count here is expected and not a failure — say so rather than let a reader
    # wonder.
    if result.get("coreSkipped"):
        print(f"note: {result['coreSkipped']} Core-owned row(s) skipped, as designed.")


def materialise(api: Api) -> None:
    """
    Import writes metadata rows. The physical tables and the entity assembly do
    not exist until these run — WorkspaceController's own doc says so.
    """
    for label, path in [
        ("schema sync", "/api/schema/sync"),
        ("compile metadata", "/api/metadata/compile"),
        ("compile models", "/api/metadata/models/compile"),
    ]:
        print(f"--- {label} ---")
        status, body = api.post(path)
        if status != 200:
            fail(f"{label} failed ({status}).", body)
        print(body[:600])


def check_compiled(api: Api) -> list[dict]:
    """THE gate. Until this existed, nothing checked that the tree even builds."""
    status, body = api.get("/api/metadata/models")
    if status != 200:
        fail(f"Could not list models ({status}).", body)
    models = json.loads(body)

    broken = [m for m in models if (m.get("compilationStatus") or "null") != "Ok"]
    if broken:
        detail = "\n".join(
            f"{m['name']}: {m.get('compilationStatus')} — "
            f"{(m.get('compilationError') or '')[:300]}" for m in broken)
        fail(f"{len(broken)} of {len(models)} model(s) did not compile.", detail)

    print(f"All {len(models)} models compiled.")
    return models


def export_bundles(api: Api, models: list[dict], excluded: set[str], out: str) -> list[dict]:
    """
    One bundle per model, WITHOUT its dependency closure.

    The unit of delivery is a model; the closure is resolved at install time,
    where the target's existing models are known. Exporting closures here would
    put GLIntegration's eight dependencies inside GLIntegration and ship most of
    the tree eight times over.
    """
    os.makedirs(out, exist_ok=True)
    index = []

    for model in sorted(models, key=lambda m: m["name"]):
        name = model["name"]
        if name in excluded:
            print(f"  skipping {name} (excluded)")
            continue

        version = model.get("modelVersion")
        if not version:
            fail(f"{name} has no modelVersion.",
                 "A model with no version cannot be delivered: nothing downstream "
                 "could tell an upgrade from a downgrade, and the MinVersion gate "
                 "would have nothing to compare. Set one in its model.json.")

        status, body = api.get(f"/api/metadata/models/{model['metaId']}/export")
        if status != 200:
            fail(f"Exporting {name} failed ({status}).", body)

        filename = f"{name}-{version}.bundle.json"
        with open(os.path.join(out, filename), "w", encoding="utf-8") as f:
            f.write(body)

        # Encode once. len(body) counts characters, and the captions are Cyrillic,
        # so the two disagree by ~16% — the first green run logged Accounting as
        # 50750 bytes for a 58763-byte file. A size that does not match the file
        # on disk is a size nobody can check anything against.
        encoded = body.encode("utf-8")
        digest = hashlib.sha256(encoded).hexdigest()
        index.append({
            "model": name,
            "version": version,
            "publisher": model.get("publisher"),
            "layerId": model.get("layerId"),
            "file": filename,
            "bytes": len(encoded),
            # Travels with the bundle to a control plane and then into a
            # customer's database. "The file I received is the file that was
            # built" should not rest on trusting the transport.
            "sha256": digest,
        })
        print(f"  {name} {version} -> {filename} ({len(encoded)} bytes)")

    if not index:
        fail("No bundles were produced — every model is excluded?")
    return index


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--base", required=True, help="http://127.0.0.1:PORT of the throwaway stand")
    p.add_argument("--root", default=".", help="workspace root ON THIS machine (for the config)")
    p.add_argument("--container-root", default="/workspace", help="the same tree as the STAND sees it")
    p.add_argument("--out", default="bundles")
    p.add_argument("--ready-timeout", type=int, default=420)
    args = p.parse_args()

    with open(os.path.join(args.root, ".zuloone-release.json"), encoding="utf-8") as f:
        config = json.load(f)
    excluded = set(config.get("exclude") or [])

    api = Api(args.base)
    wait_ready(api, args.ready_timeout)
    seed_admin(api)
    import_workspace(api, args.container_root)
    materialise(api)
    models = check_compiled(api)
    index = export_bundles(api, models, excluded, args.out)

    with open(os.path.join(args.out, "index.json"), "w", encoding="utf-8") as f:
        json.dump({"models": index}, f, indent=2)
    print(f"{len(index)} bundle(s) written to {args.out}/")


if __name__ == "__main__":
    main()
