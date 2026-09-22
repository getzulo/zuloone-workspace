#!/usr/bin/env python3
"""
Seed the stand the way a tenant is seeded, then run the business test suite on
it. Fails the build on any failing test.

Runs on the SAME throwaway PostgreSQL stand that build_bundles.py just used, in
the same job, immediately after it. That placement is the whole point: RMS runs
PostgreSQL, the dev stand and every test run on SQL Server, and until
2026-09-22 nothing had ever executed the suite against the engine production
uses. When it finally was, PostgreSQL answered 629/22 where SQL Server answered
649/0, and nine of those failures were real defects — a boolean predicate
translated as `[Column] = 1`, which raises 42883 against a real boolean and so
made every script filtering on a flag silently match nothing in production.

    run_suite.py --base http://127.0.0.1:18080

TWO THINGS THE STAND NEEDS, or the run is red for reasons that are not the
code's:

  * the data packages have to be APPLIED. import-all brings metadata only; the
    seeded contour — tax codes, rates, module settings — ships separately and a
    tenant gets it at install. Without it nine Ukrainian tests report
    «код UA-S найдено 0», which reads as a broken seed rather than an unseeded
    stand.
  * the container needs ASPNETCORE_ENVIRONMENT=Docker and a `core` network
    alias. The fatoora channels and the `fatoora-csid` credential live in
    appsettings.Docker.json — .NET's environment-variable provider cannot
    express a hyphenated key — and those channels dial http://core:8080 by the
    compose container's name.

Both are set in ci.yml next to the container, not here, because they are
properties of the stand rather than of this script.
"""

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from build_bundles import Api, fail  # noqa: E402


def apply_data_packages(api: Api) -> None:
    """
    Apply every package the stand offers, and READ THE ANSWER.

    A 200 is not success here and neither is a zero exit code from the caller.
    The first version of this loop in a throwaway harness counted curl's exit
    status, reported "13 of 13 applied", and every single call had in fact been
    rejected 400 — the id carried a stray CR from a Windows-written list. The
    suite then failed nine tests for a seed that was never there.
    """
    status, body = api.get("/api/data-packages")
    if status != 200:
        fail(f"Could not list data packages ({status}).", body)
    packages = json.loads(body)
    if not packages:
        fail("The stand offers no data packages — the seed would be empty.")

    applied, failures = 0, []
    for pack in packages:
        pid = pack.get("id")
        if not pid:
            continue
        status, body = api.post("/api/data-packages/apply", {"id": pid})
        ok = status == 200 and json.loads(body or "{}").get("ok") is True
        if ok:
            applied += 1
        else:
            failures.append(f"{pid}: {status} {body[:200]}")

    print(f"Data packages applied: {applied} of {len(packages)}")
    if failures:
        fail("Some data packages did not apply:", "\n".join(failures))


def run_suite(api: Api) -> None:
    status, body = api.post("/api/metadata/tests/run-all", {}, timeout=2400)
    if status != 200:
        fail(f"run-all did not answer ({status}).", body)
    result = json.loads(body)
    passed = result.get("totalPassed")
    failed = result.get("totalFailed")
    print(f"PostgreSQL suite: {passed} passed, {failed} failed")

    if not failed:
        if not passed:
            fail("The suite reported no tests at all — it did not run.")
        return

    # Name every failure. A count alone sends the reader to a 25-minute local
    # reproduction to find out which test broke.
    lines = []
    for script in result.get("scripts") or []:
        if script.get("compileError"):
            lines.append(f"  {script.get('testName')}: {script['compileError'][:300]}")
        for case in script.get("cases") or []:
            if str(case.get("outcome", "")).lower() in ("passed", "skipped"):
                continue
            detail = str(case.get("errorDetail") or "").replace("\n", " ")[:300]
            lines.append(f"  {script.get('testName')} :: {case.get('name')}\n      {detail}")
    fail(f"{failed} test(s) failed on PostgreSQL.", "\n".join(lines))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    args = parser.parse_args()

    api = Api(args.base)
    # build_bundles.py already seeded the administrator earlier in this job, and
    # /api/auth/setup is one-shot — so log in rather than seed. The credentials
    # are its, deliberately: two sets of CI credentials would be one set too many.
    #
    # LoginRequest takes NAME and password, not the email that seeding also asked
    # for. Sending an email here answers 400, and an earlier version of this
    # script then fell through to seed_admin and failed with "Setup has already
    # been completed" — a confusing way to report a wrong field name. A failed
    # login is now fatal on the spot.
    status, body = api.post("/api/auth/login", {
        "name": "ci", "password": "workspace-ci-not-a-secret",
    })
    if status != 200:
        fail(f"Could not log in as the CI administrator ({status}).", body)
    api.token = json.loads(body).get("token")
    if not api.token:
        fail("The login call returned no token.", body)

    apply_data_packages(api)
    run_suite(api)


if __name__ == "__main__":
    main()
