import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const base=new URL('../',import.meta.url);
const catalogs=JSON.parse(readFileSync(new URL('locales/catalogs.json',base)));
let checks=0;
const ok=(yes,name)=>{assert.ok(yes,name);checks++};
for(const [lang,mapping] of Object.entries(catalogs)){
 ok(Object.keys(mapping).length===197,lang+' complete source coverage');
 for(const [source,target] of Object.entries(mapping)){
  ok(target.trim().length>0,lang+' nonempty');
  ok(!/[\u0590-\u05ff]/.test(target),lang+' no unintended Hebrew');
  ok(!/ZXQ.*QXZ/.test(target),lang+' no preparation markers');
  if(source.includes('Start-app'))ok(target.includes('Start-app'),lang+' literal brand retained');
  for(const token of ['Windows','2025','10','http/https','.NET','SHA-256','Chrome','Edge','Brave','ZIP','DLL','sha256'])if(source.includes(token))ok(target.includes(token),lang+' token '+token);
  for(const placeholder of ['{count}','{total}'])if(source.includes(placeholder))ok(target.includes(placeholder),lang+' count placeholder');
 }
 for(const page of ['index','docs']){
  const folder=lang==='zh-CN'?'zh-Hans':lang;
  const html=readFileSync(new URL(folder+'/'+page+'.html',base),'utf8');
  ok(html.includes('lang="'+folder+'"')&&html.includes('dir="ltr"'),lang+' page semantics');
  ok(html.includes('data-site-root=".."'),lang+' shared pointer root');
  ok(!/releases\/download/.test(html),lang+' no static download bypass');
 }
}
console.log(checks+' i18n static checks passed');
