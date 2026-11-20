#!/usr/bin/env python3
"""Stage only the pinned, previously publication-approved website tree.

Trusted publisher code and this pin come from repository default main.
Website branch code is data, never executed by the publisher.
No network or repository writes occur in this module.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess

BRANCH = 'website/main'
SHA = re.compile(r'[0-9a-f]{40}')


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args])


def stage(repo, pin_path, out):
    pin = json.loads(Path(pin_path).read_text())
    if (set(pin) != {'schema', 'branch', 'commit'} or type(pin['schema']) is not int
            or pin['schema'] != 1 or pin['branch'] != BRANCH
            or not isinstance(pin['commit'], str) or not SHA.fullmatch(pin['commit'])):
        raise ValueError('invalid publication-approved website pin')
    commit = pin['commit']
    if git(repo, 'cat-file', '-t', commit).strip() != b'commit':
        raise ValueError('pin is not a commit')
    subprocess.run(['git', '-C', str(repo), 'merge-base', '--is-ancestor', commit,
                    'refs/remotes/origin/' + BRANCH], check=True)
    tree = git(repo, 'rev-parse', commit + ':website').decode().strip()
    entries = git(repo, 'ls-tree', '-r', '-z', commit, '--', 'website').split(b'\0')
    files = {}
    payload = []
    for entry in filter(None, entries):
        metadata, raw_path = entry.split(b'\t', 1)
        mode, kind, blob = metadata.decode().split()
        name = raw_path.decode('utf-8', errors='strict')
        path = PurePosixPath(name)
        if path.parts[0] != 'website' or '..' in path.parts or mode not in ('100644', '100755') or kind != 'blob':
            raise ValueError('unsafe website entry')
        relative = path.relative_to('website')
        # Tests never enter the Pages artifact; match update_pointer.py.
        if relative.parts[0] == 'tests':
            continue
        if str(relative) in ('pointer.json', 'downloads.json', 'website-source.json'):
            # Pointer/catalog are always regenerated from approved releases.
            continue
        data = git(repo, 'cat-file', 'blob', blob)
        files[str(relative)] = hashlib.sha256(data).hexdigest()
        payload.append((relative, data))
    if not {'index.html', 'docs.html', 'styles.css'}.issubset(files):
        raise ValueError('website is incomplete')
    out = Path(out)
    if out.exists():
        raise ValueError('stage destination already exists')
    # Validate every path and required file before writing any bytes.
    for relative, data in payload:
        dest = out / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
    identity = {'schema': 1, 'branch': BRANCH, 'commit': commit, 'tree': tree, 'files': files}
    (out / 'website-source.json').write_text(json.dumps(identity, sort_keys=True, indent=2) + '\n')
    return identity


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--repo', default='.')
    p.add_argument('--pin', required=True)
    p.add_argument('--out', required=True)
    a = p.parse_args()
    identity = stage(a.repo, a.pin, a.out)
    print(json.dumps({k: identity[k] for k in ('commit', 'tree', 'branch')}))


if __name__ == '__main__':
    main()
