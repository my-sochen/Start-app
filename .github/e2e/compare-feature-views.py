"""Exact destination-baseline pixels and identical native keyboard traces."""
import json
from pathlib import Path
from PIL import Image, ImageChops
root = Path('evidence/feature-views')
before = root / 'before'
rows = []
for variant in ('before-repeat', 'after'):
    candidate = root / variant
    assert sorted(p.name for p in before.glob('*.png')) == sorted(p.name for p in candidate.glob('*.png'))
    assert len(list(before.glob('*.png'))) == 8
    for image in sorted(before.glob('*.png')):
        a, b = Image.open(image).convert('RGBA'), Image.open(candidate / image.name).convert('RGBA')
        assert a.size == b.size
        difference = sum(any(pixel) for pixel in ImageChops.difference(a, b).getdata())
        rows.append(dict(variant=variant, state=image.name, pixels=difference))
        assert difference == 0, rows[-1]
    for name in ('checks.json', 'focus.json'):
        assert json.loads((before/name).read_text()) == json.loads((candidate/name).read_text()), (variant, name)
    assert all(c['ok'] for c in json.loads((candidate/'checks.json').read_text()))
(root/'comparison.json').write_text(json.dumps(rows, indent=2))
print('8 feature states exact; baseline repeats exact; keyboard focus traces and binding/close checks identical and pass')
