"""Fixed-candidate native journey evidence, not final-ZIP or broad acceptance."""
from collections import Counter
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

SHA = '72cece944eaef8d6057d702db3ae66c2bbf9290e'
TREE = 'b93dd94396eb6db119723a8a411168c1c80ee688'
LANES = {
    'ux': ('tests/StartApp.OpeningUiCapture/bin/Release/net10.0-windows', 'StartApp.OpeningUiCapture', 'out-prototype-ux/checks.json'),
    'capture-policy': ('tests/StartApp.NativeCapturePolicy/bin/Release/net10.0-windows', 'StartApp.NativeCapturePolicy', 'diag/native-policy-assertions.json'),
}


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args], text=True).strip()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def outcomes(path, expected):
    rows = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(rows, list) or not rows:
        raise ValueError('Missing functional action/outcome records')
    for row in rows:
        if not isinstance(row, dict) or not isinstance(row.get('name'), str) or not isinstance(row.get('ok'), bool):
            raise ValueError('Invalid functional result record')
    if not all(row['ok'] for row in rows):
        raise ValueError('Functional case failed; inspect native artifact records and pixels')
    actual = Counter(row['name'] for row in rows)
    if actual != Counter(expected):
        raise ValueError('Functional case inventory mismatch: missing/duplicate/unrelated records')
    return rows



def process_evidence(path, binary_folder, name, records):
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    for field, filename in [('exeSha256', name + '.exe'), ('dllSha256', name + '.dll'), ('appDllSha256', 'StartApp.App.dll')]:
        if data[field].lower() != digest(binary_folder / filename):
            raise ValueError('Executed binary identity mismatch: ' + field)
    if data.get('exitCode') != 0 or not (data.get('pid') or data.get('helperPid')):
        raise ValueError('Missing successful process provenance')
    if data.get('assertionsSha256', '').lower() != digest(records):
        raise ValueError('Fresh process outcomes digest mismatch')
    return data


def changed_draft_evidence(output, workflow):
    source = workflow / 'tools/FunctionalJourneys/Program.cs'
    # Fixed explicit names, not a count or labels accepted from current output.
    expected_write = [
        'write added rows remain normal', 'write real bound staged fields changed',
        'write changed staged draft not persisted',
        'write Done collapses without losing changed draft', 'write Done does not save',
        'write reopen retains changed bound fields',
        'write second Edit isolates one editor and retains first draft',
        'write switch back retains changed draft', 'write Save clears staged rows',
        'write staged changed values persisted', 'write saved row edited values persisted',
        'write no resource launches',
    ]
    expected_reload = [
        'reload staged changed values persisted', 'reload saved row edited values persisted',
        'reload visible rows contain persisted staged and saved edits', 'reload no resource launches',
    ]
    folder = workflow / 'tools/FunctionalJourneys/bin/Release/net10.0-windows'
    records = {}
    for phase, names in [('write', expected_write), ('reload', expected_reload)]:
        records[phase] = outcomes(output / (phase + '-checks.json'), names)
        data = json.loads((output / (phase + '-process.json')).read_text(encoding='utf-8-sig'))
        if data['phase'] != phase or data['testDllSha256'] != digest(folder / 'FunctionalJourneys.dll') or data['exeSha256'] != digest(folder / 'FunctionalJourneys.exe') or data['appDllSha256'] != digest(folder / 'StartApp.App.dll'):
            raise ValueError('Changed-draft executed assembly mismatch')
        records[phase + 'Process'] = data
    if records['writeProcess']['pid'] == records['reloadProcess']['pid'] or records['reloadProcess']['startUtc'] <= records['writeProcess']['startUtc']:
        raise ValueError('Fresh separate reload process not established')
    records['testSourceSha256'] = digest(source)
    records['exeSha256'] = digest(folder / 'FunctionalJourneys.exe')
    records['testAssemblySha256'] = digest(folder / 'FunctionalJourneys.dll')
    records['loadedAppDllSha256'] = digest(folder / 'StartApp.App.dll')
    records['storeSha256'] = digest(output / 'isolated-store.json')
    return records

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--lane', choices=LANES, required=True)
    ap.add_argument('--phase', choices=['before', 'after'], required=True)
    ap.add_argument('--root', type=Path, required=True)
    ap.add_argument('--output', type=Path, required=True)
    a = ap.parse_args()
    a.output.mkdir(parents=True, exist_ok=True)
    workflow = Path(__file__).resolve().parents[2]
    identity = {
        'testedCommit': git(a.root, 'rev-parse', 'HEAD'),
        'testedTree': git(a.root, 'rev-parse', 'HEAD^{tree}'),
        'workflowCommit': git(workflow, 'rev-parse', 'HEAD'),
        'workflowTree': git(workflow, 'rev-parse', 'HEAD^{tree}'),
        'runId': os.environ['GITHUB_RUN_ID'], 'runAttempt': os.environ['GITHUB_RUN_ATTEMPT'],
        'lane': a.lane, 'phase': a.phase,
        'sdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
        'runnerOs': os.environ['RUNNER_OS'], 'runnerImage': os.environ.get('ImageVersion'),
        'compileProbeConstants': ['STARTAPP_CAPTURE_TRACE', 'STARTAPP_CAPTURE_TEST'] if a.lane == 'capture-policy' else [],
        'productIdentity': 'Unstamped test build; not the TEST full pipeline final packaged binary.',
        'scope': 'Existing native component/journey harness on fixed source. Isolated UX adapters or interleaving probes. Not independent human exploration, consumer Windows, final ZIP, publisher/site or beta enrollment QA.',
    }
    identity['identityValid'] = identity['testedCommit'] == SHA and identity['testedTree'] == TREE and identity['workflowCommit'] == os.environ['GITHUB_SHA']
    identity['trackedClean'] = not git(a.root, 'status', '--porcelain', '--untracked-files=no')
    paths = git(a.root, 'ls-files').splitlines()
    identity['candidateSourceBytesSha256'] = {
        p: digest(a.root / p) for p in paths
        if p.startswith(('src/', 'tests/', '.github/e2e/')) and (a.root / p).is_file()
    }
    identity['workflowSourceBytesSha256'] = {
        p: digest(workflow / p) for p in (
            '.github/workflows/functional-qualification-72ce.yml',
            '.github/e2e/functional-qualification.py',
            'tests/functional-qualification/test_qualification.py',
            '.github/e2e/functional-case-inventory.json',
            'tools/FunctionalJourneys/Program.cs',
            'tools/FunctionalJourneys/FunctionalJourneys.csproj',
            'tools/functional-qualification/Require-CleanChromium.ps1',
            'tools/functional-qualification/Run-CapturePolicy.ps1',
        )
    }
    failed = not identity['identityValid'] or not identity['trackedClean']
    if a.phase == 'after':
        folder, name, records = LANES[a.lane]
        binaries = [a.root / folder / (name + suffix) for suffix in ('.exe', '.dll')]
        binaries += [a.root / folder / 'StartApp.App.dll']
        identity['executablesSha256'] = {str(p.relative_to(a.root)): digest(p) for p in binaries if p.is_file()}
        identity['allExpectedBinariesPresent'] = all(p.is_file() for p in binaries)
        failed |= not identity['allExpectedBinariesPresent']
        try:
            inventory = json.loads((workflow / '.github/e2e/functional-case-inventory.json').read_text(encoding='utf-8'))
            if inventory['testedCommit'] != SHA or inventory['testedTree'] != TREE:
                raise ValueError('Inventory candidate binding mismatch')
            for source, expected_hash in inventory['rawSourceSha256'].items():
                # Windows checkout CRLF is recorded separately; bind expected
                # source to exact raw Git blob bytes as well.
                blob = subprocess.check_output(['git', '-C', str(a.root), 'show', 'HEAD:' + source])
                if hashlib.sha256(blob).hexdigest() != expected_hash:
                    raise ValueError('Case inventory source mismatch: ' + source)
            rows = outcomes(a.root / records, inventory['lanes'][a.lane]['expectedNameCounts'])
            (a.output / 'actual-functional-outcomes.json').write_text(json.dumps(rows, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            identity['outcomeRecordCount'] = len(rows)
            process_path = a.output / 'ux-process.json' if a.lane == 'ux' else a.root / 'diag/policy-process.json'
            identity['actualProcess'] = process_evidence(process_path, a.root / folder, name, a.root / records)
            if a.lane == 'ux':
                identity['changedDraft'] = changed_draft_evidence(a.output / 'changed-draft', workflow)
            else:
                preflight = json.loads((a.output / 'clean-Chromium.json').read_text(encoding='utf-8-sig'))
                if not preflight['clean'] or not preflight['enumerationSucceeded'] or preflight['classReadErrors'] or preflight['visibleChromiumHandles']:
                    raise ValueError('Disposable clean Chromium precondition missing')
                identity['cleanChromiumPrecondition'] = preflight
                # Exact inventory includes observer-fired/visits/cancellation assertions.
                # These cannot pass when probes are compiled out or inactive.
                identity['activeProbeEvidence'] = [row['name'] for row in rows if any(term in row['name'] for term in ('actually injected', 'boundary', 'visits every snapshot'))]
                if len(identity['activeProbeEvidence']) != 22:
                    raise ValueError('Active interleaving proof inventory mismatch')

            identity['outcomeStatus'] = 'all supplied existing-harness records passed; inspect actions and coverage, not count alone'
        except (OSError, ValueError, TypeError, KeyError) as error:
            identity['outcomeStatus'] = str(error)
            failed = True
    (a.output / (a.phase + '-binding.json')).write_text(json.dumps(identity, indent=2) + '\n', encoding='utf-8')
    if failed:
        raise SystemExit('Qualification evidence incomplete/failed; not a pass')
    print('Fixed-candidate ' + a.phase + ' evidence written. Scope stays native harness, not final ZIP/full functional QA.')


if __name__ == '__main__':
    main()
