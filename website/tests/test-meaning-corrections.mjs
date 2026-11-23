// Focused Hebrew-source meaning regression. Not native-language certification.
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
const root=new URL('../',import.meta.url);
const {sources,expected}=JSON.parse(readFileSync(new URL('tests/meaning-corrections.json',root)));
const catalogs=JSON.parse(readFileSync(new URL('locales/catalogs.json',root)));
const runtime=readFileSync(new URL('i18n.js',root),'utf8');
let count=0;
for(const [lang,values] of Object.entries(expected)){
 const folder=lang==='zh-CN'?'zh-Hans':lang;
 const html=readFileSync(new URL(folder+'/docs.html',root),'utf8');
 const context={document:{documentElement:{lang:folder}},window:{}};
 vm.runInNewContext(runtime,context);
 for(const [i,source] of sources.entries()){
  const target=values[i];
  assert.equal(catalogs[lang][source],target,lang+' catalog meaning '+i);count++;
  assert.equal(context.window.StartAppI18n.t(source),target,lang+' runtime meaning '+i);count++;
  assert.ok(html.replaceAll('&#x27;',"'").replaceAll('&quot;','"').replaceAll('&amp;','&').replaceAll('&lt;','<').replaceAll('&gt;','>').includes(target),lang+' static meaning '+i);count++;
 }
}
console.log(count+' focused meaning consistency checks passed');
