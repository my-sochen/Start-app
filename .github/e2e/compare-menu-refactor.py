import json
from pathlib import Path
from PIL import Image, ImageChops
root=Path('evidence')
rows=[]
for variant in ['before-repeat','after']:
    assert json.loads((root/'before/menus.json').read_text())==json.loads((root/variant/'menus.json').read_text()),f'Menu geometry or entries differ:{variant}'
    images=sorted((root/'before').glob('*.png'))
    assert len(images)==8,images
    for p in images:
        a=Image.open(p).convert('RGBA');b=Image.open(root/variant/p.name).convert('RGBA')
        diff=sum(any(pixel) for pixel in ImageChops.difference(a,b).getdata()) if a.size==b.size else -1
        rows.append(dict(variant=variant,state=p.name,pixels=diff))
(root/'menu-comparison.json').write_text(json.dumps(rows,indent=2))
assert all(r['pixels']==0 for r in rows),rows
print('8menu/submenu states: baseline repeat and candidate entries,geometry and pixels exactly match')
