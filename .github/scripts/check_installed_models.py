#!/usr/bin/env python3
"""Check the upgraded database, not just the aggregate importer row count."""

import json
from pathlib import Path
import subprocess
import sys


def problems(expected: list[dict], installed: list[dict]) -> list[str]:
    by_name = {model['name']: model for model in installed}
    errors = []
    for model in expected:
        name = model['name']
        actual = by_name.get(name)
        if actual is None:
            errors.append(f'{name}: model missing after upgrade')
            continue
        if not actual.get('enabled') or actual.get('status') != 'Ok':
            errors.append(f"{name}: enabled={actual.get('enabled')}, "
                          f"compile={actual.get('status')} — {actual.get('error') or ''}")
        # Core follows the platform version, not the exported model.json version.
        if model.get('isSystem'):
            continue
        want = model['modelVersion']
        if actual.get('version') != want or actual.get('packageVersion') != want:
            errors.append(f"{name}: expected {want}, model={actual.get('version')}, "
                          f"package={actual.get('packageVersion')}")
    return errors


def main() -> int:
    container, root = sys.argv[1:]
    expected = [json.loads(p.read_text(encoding='utf-8'))['object']
                for p in sorted(Path(root).glob('*/model.json'))]
    if not expected:
        print('::error::No staged models to verify.')
        return 1
    query = '''
        SELECT COALESCE(json_agg(json_build_object(
            'name', m."Name", 'version', m."ModelVersion",
            'enabled', m."IsEnabled", 'status', m."CompilationStatus",
            'error', m."CompilationError", 'packageVersion', p."PackageVersion"
        )), '[]')
        FROM "MetaModels" m
        LEFT JOIN "MetaPackages" p ON p."Name" = m."Name";
    '''
    result = subprocess.run(
        ['docker', 'exec', container, 'psql', '-X', '-U', 'ug', '-d', 'ug',
         '-v', 'ON_ERROR_STOP=1', '-At', '-c', query],
        check=True, stdout=subprocess.PIPE, text=True, encoding='utf-8',
    )
    errors = problems(expected, json.loads(result.stdout))
    for error in errors:
        print(f'::error::{error}')
    if errors:
        return 1
    print(f'Installed models: {len(expected)} enabled and compiled; '
          'all business model and package versions match the staged tree.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
