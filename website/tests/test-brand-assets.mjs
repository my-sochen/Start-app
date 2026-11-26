import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
const root=path.resolve(import.meta.dirname,'..');
const expected='d1e6ed51ab5ac5949883bc6f3f2e3b14156dac57482db22ca1b53b4a4e7e94d8';
for(const n of ['header-ufo.svg','favicon.svg'])assert.equal(createHash('sha256').update(fs.readFileSync(root+'/icons/'+n)).digest('hex'),expected,n+' exact approved blue app SVG');
let count=0;
function walk(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){if(e.name==='node_modules')continue;const p=path.join(dir,e.name);if(e.isDirectory())walk(p);else if(p.endsWith('.html')){const s=fs.readFileSync(p,'utf8');if(!s.includes('class="brand"'))continue;count++;assert.match(s,/<i aria-hidden="true"><img src="[^"]*icons\/header-ufo.svg" width="29" height="29" alt="" \/><\/i>/);assert.match(s,/href="[^"]*icons\/favicon.svg\?v=blue-trace"/);assert(!/<a[^>]*class="brand"[^>]*><i[^>]*><svg/.test(s),'no old embedded mark');}}}walk(root);assert.equal(count,26);console.log('26 page brand checks and 2 exact approved asset hashes passed');
