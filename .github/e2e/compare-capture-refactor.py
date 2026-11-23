import json
from pathlib import Path
from PIL import Image, ImageChops
root = Path('evidence')
images = sorted(p.name for p in (root / 'before').glob('*.png') if '-desktop' not in p.name)
assert len(images) == 9, f'Expected 9 fixed states, found {images}'
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
