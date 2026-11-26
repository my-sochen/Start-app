import json
from pathlib import Path
from PIL import Image, ImageChops
root = Path('evidence')
images = [name+'.png' for name in ['01-new-form','02-pick-environment','03-side-empty','04-staged-summary','05-staged-editor','06-saved-side','07-grouped-duplicates','08-drag-duplicate','09-minimum-size']]
images += ['frame-'+str(i).zfill(2)+'.png' for i in range(1,14)] + ['prompt-notice.png','prompt-conflict.png','prompt-new-workspace.png']
capture_names=set(images[:9])
# Same-process is an additional exact gate, never a substitute for the independent baseline control.
shared = json.loads((root / 'after/same-process.json').read_text())
assert len(shared) == 9 and {r['state'] + '.png' for r in shared} == capture_names, shared
assert all(r['different_pixels'] == 0 for r in shared), shared
print('9 original-Grid versus extracted-view states exactly match in one process', flush=True)
# A baseline repeat is an exactness control, not permission to ignore unstable pixels.
control = []
for name in images:
    first = Image.open(root / 'before' / name).convert('RGBA')
    second = Image.open(root / 'before-repeat' / name).convert('RGBA')
    count = sum(any(p) for p in ImageChops.difference(first, second).getdata()) if first.size == second.size else -1
    control.append(dict(state=name, different_pixels=count))
(root / 'baseline-repeat-comparison.json').write_text(json.dumps(control, indent=2))
assert all(r['different_pixels'] == 0 for r in control), f'Baseline is not deterministic: {control}'
results = []
for name in images:
    before = Image.open(root / 'before' / name).convert('RGBA')
    after = Image.open(root / 'after' / name).convert('RGBA')
    same_size = before.size == after.size
    diff = ImageChops.difference(before, after) if same_size else None
    different = sum(any(pixel) for pixel in diff.getdata()) if diff else -1
    results.append(dict(state=name, same_size=same_size, different_pixels=different))
    if different:
        diff.save(root / ('DIFF-' + name)) if diff else None
(root / 'pixel-comparison.json').write_text(json.dumps(results, indent=2))
assert all(r['same_size'] and r['different_pixels'] == 0 for r in results), results
before = json.loads((root / 'before/results.json').read_text())
after = json.loads((root / 'after/results.json').read_text())
assert before == after and all(r['ok'] for r in after), (before, after)
print(f'{len(results)} states pixel-identical; {len(after)} behavior checks identical and pass')
