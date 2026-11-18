"""Exact-ref profiles and same-run source/command evidence. Never a pass cache."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
# This list must match workflow triggers. New or unclassified permitted feature
# refs get full validation explicitly; there is no impact-based fast inference.
FULL_REFS = {
    'feat/validation-profiles-v1', 'fix/notice-validation-binding',
    'feat/prototype-ux-capture-integration', 'fix/capture-ui-readiness',
    'fix/native-picker-readiness', 'feat/update-settings-placement',
    'feat/approved-update-notifier', 'feat/portable-release-wiring-local',
    'feat/portable-candidate-integration', 'feat/per-instance-items',
    'feat/browser-icon-display', 'feat/release-integration-unified',
    'test/validation',
}


def profile_for(ref):
    if ref == 'refs/heads/dev':
        return 'fast'
    if ref in {'refs/heads/' + branch for branch in FULL_REFS}:
        return 'full'
    raise ValueError('Unexpected ref: add an explicit reviewed full route, never infer fast')


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def binding(ref):
    sha = git('rev-parse', 'HEAD')
    if os.environ.get('SHA') != sha:
        raise ValueError('SHA must be the exact current checkout')
    if git('status', '--porcelain', '--untracked-files=no'):
        raise ValueError('Tracked checkout changes invalidate source binding')
    paths = git('ls-files').splitlines()
    definitions = [p for p in paths if (
        p.startswith(('.github/workflows/', '.github/e2e/', '.github/release/', 'tests/', 'tools/portable-entry/'))
        or p.endswith(('.csproj', '.props', '.targets', '.slnx'))
        or Path(p).name in {'global.json', 'NuGet.Config', 'nuget.config', 'packages.lock.json'}
    )]
    return {
        'schema': 1, 'sourceCommit': sha, 'sourceTree': git('rev-parse', 'HEAD^{tree}'),
        'ref': ref, 'profile': profile_for(ref),
        'runId': os.environ['GITHUB_RUN_ID'], 'runAttempt': os.environ['GITHUB_RUN_ATTEMPT'],
        'sdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
        'runnerOs': os.environ['RUNNER_OS'], 'runnerImage': os.environ.get('ImageVersion'),
        'captureVersion': '0.1.0-proto.2', 'packageVersion': os.environ['VERSION'],
        'definitionSha256': {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in sorted(definitions)},
        'scope': 'Source/command identity only. Not a pass receipt, human QA or publication authority.',
        'artifactRetentionDays': 5,
    }


def result_message(profile, resolve, tests, ui, package):
    if (resolve, tests, ui) != ('success', 'success', 'success'):
        raise ValueError('Missing, failed, cancelled or skipped required checks')
    if profile == 'fast' and package == 'skipped':
        return 'DEV fast subset passed. NOT full QA. No candidate package.'
    if profile == 'full' and package == 'success':
        return 'Full automation passed for this run. Exact final ZIP still needs reviewer QA and owner approval. No publication.'
    raise ValueError('Unknown profile or invalid required package result')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--ref')
    parser.add_argument('--status', action='store_true')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--evidence', type=Path)
    parser.add_argument('--zip', type=Path)
    args = parser.parse_args()
    if args.status:
        message = result_message(*(os.environ.get(k) for k in ('PROFILE', 'RESOLVE', 'TESTS', 'UI', 'PACKAGE')))
        with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a', encoding='utf-8') as out:
            out.write(message + '\n')
        print(message)
        return
    profile = profile_for(args.ref)
    if args.evidence:
        evidence = binding(args.ref)
        if args.zip:
            evidence['finalZip'] = {'name': args.zip.name, 'size': args.zip.stat().st_size,
                                    'sha256': hashlib.sha256(args.zip.read_bytes()).hexdigest()}
        args.evidence.write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
    if args.output:
        with args.output.open('a', encoding='utf-8') as out:
            out.write('profile=' + profile + '\n')
    print('profile=' + profile + (' (subset only; no candidate/QA)' if profile == 'fast' else ' (full automation; human QA separate)'))


if __name__ == '__main__':
    main()
