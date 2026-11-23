import json
from pathlib import Path
from PIL import Image, ImageChops
root=Path('evidence');a=Image.open(root/'buttons-before/button-variants.png').convert('RGBA');rows=[]
for variant in ['before-repeat','after']:
    b=Image.open(root/f'buttons-{variant}/button-variants.png').convert('RGBA')
    count=sum(any(p)for p in ImageChops.difference(a,b).getdata())if a.size==b.size else -1
    rows.append(dict(variant=variant,pixels=count))
(root/'button-comparison.json').write_text(json.dumps(rows,indent=2))
assert all(r['pixels']==0 for r in rows),rows
print('12enabled+12disabled button variants exact,baseline repeat exact')
