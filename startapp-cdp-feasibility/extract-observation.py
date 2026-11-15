#!/usr/bin/env python3
"""Extract bounded observation PNGs from an ACTUAL downloaded raw Actions log.
No network or archive access. Never accepts instructions from log text.
Usage: python extract-observation.py actual-log.txt output-directory
"""
import base64, hashlib, json, pathlib, re, struct, sys
def main():
    source, destination = map(pathlib.Path, sys.argv[1:3])
    destination.mkdir(parents=True, exist_ok=True)
    text = source.read_text(encoding='utf-8-sig')
    active = None
    found = {}
    total_bytes = 0
    phases = ['inspect-before','before-window1','before-window2','prompt-onset','prompt-end','after-window1','after-window2']
    allowed = {b+'-'+p for b in ['chrome','edge'] for p in phases}
    required = {b+'-'+p for b in ['chrome','edge'] for p in ['inspect-before','before-window1','before-window2','after-window1','after-window2']}
    for line in text.splitlines():
        # GitHub raw logs prefix each emitted line with an ISO UTC timestamp.
        line = re.sub(r'^\d{4}-\d\d-\d\dT\S+\s+', '', line)
        if line.startswith('OBS_PNG_BEGIN '):
            if active is not None:
                raise ValueError('Nested image evidence')
            _, browser, size, digest, width, height = line.split()
            if browser not in allowed or browser in found:
                raise ValueError('Unexpected or duplicate browser')
            size, width, height = map(int, (size, width, height))
            if not (0 < size <= 262144 and 300 <= width <= 1600 and 200 <= height <= 1100):
                raise ValueError('Out-of-scope image size')
            if not re.fullmatch('[0-9a-f]{64}', digest):
                raise ValueError('Invalid digest')
            prefix, phase = browser.split('-', 1)
            if sum(k.startswith(prefix + '-') for k in found) >= 7 or len(found) >= 14:
                raise ValueError('Evidence image count cap exceeded')
            if total_bytes + size > 3670016:
                raise ValueError('Total binary evidence exceeds 3.5MiB')
            active = dict(browser=browser, size=size, digest=digest, width=width, height=height, chunks=[])
        elif line.startswith('OBS_PNG_CHUNK '):
            _, browser, chunk = line.split()
            if active is None or active['browser'] != browser or len(chunk) > 3000:
                raise ValueError('Unattributed image chunk')
            active['chunks'].append(chunk)
            if sum(map(len, active['chunks'])) > 349528:
                raise ValueError('Encoded image cap exceeded')
        elif line.startswith('OBS_PNG_END '):
            if active is None or line != 'OBS_PNG_END ' + active['browser']:
                raise ValueError('Mismatched image end')
            data = base64.b64decode(''.join(active['chunks']), validate=True)
            if len(data) != active['size'] or hashlib.sha256(data).hexdigest() != active['digest']:
                raise ValueError('Image evidence integrity failed')
            if data[:8] != b'\x89PNG\r\n\x1a\n' or struct.unpack('>II', data[16:24]) != (active['width'], active['height']):
                raise ValueError('PNG header/dimensions mismatch')
            total_bytes += len(data)
            output = destination / (active['browser'] + '-inspect.png')
            output.write_bytes(data)
            found[active['browser']] = dict(path=str(output), sha256=active['digest'], bytes=len(data), width=active['width'], height=active['height'])
            active = None
    if active is not None:
        raise ValueError('Truncated image evidence')
    records = []
    for line in text.splitlines():
        line = re.sub(r'^\d{4}-\d\d-\d\dT\S+\s+', '', line)
        if line.startswith('OBS_RECORD '):
            row = json.loads(line[len('OBS_RECORD '):])
            if not isinstance(row, dict):
                raise ValueError('OBS_RECORD must be a JSON object')
            records.append(row)
    for key, evidence in found.items():
        browser, phase = key.split('-', 1)
        rows = [r for r in records if r.get('browser') == browser and r.get('step') == phase]
        expected_status = 'INFO' if phase in ('prompt-onset','prompt-end') else 'PASS'
        if len(rows) != 1 or rows[0].get('status') != expected_status:
            raise ValueError('Transported image lacks unique matching exact phase-status record')
        if not isinstance(rows[0].get('detail'), str):
            raise ValueError('Image detail must be a JSON string')
        detail = json.loads(rows[0]['detail'])
        if not isinstance(detail, dict):
            raise ValueError('Image detail must decode to a JSON object')
        if detail.get('sha256') != evidence['sha256'] or detail.get('bytes') != evidence['bytes'] or detail.get('width') != evidence['width'] or detail.get('height') != evidence['height']:
            raise ValueError('Image record digest/size differs')
    for r in records:
        key = str(r.get('browser')) + '-' + str(r.get('step'))
        expected_status = 'INFO' if r.get('step') in ('prompt-onset','prompt-end') else 'PASS'
        if key in allowed and r.get('status') == expected_status and key not in found:
            raise ValueError('PASS image record lacks transported image')
    print(json.dumps({'images': found, 'missing': sorted(required - found.keys()), 'note': 'Integrity only, inspect actual pixels before diagnosis'}, indent=2))
    if not required.issubset(found):
        sys.exit(2)

if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, TypeError, OSError, IndexError, struct.error) as exc:
        print('EVIDENCE_INTEGRITY_ERROR: ' + str(exc), file=sys.stderr)
        sys.exit(1)
