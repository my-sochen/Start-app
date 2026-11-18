import {createRequire} from 'node:module';
import {readFileSync} from 'node:fs';
import assert from 'node:assert/strict';
const require=createRequire(import.meta.url),P=require('../download-pointer.js'),C=require('../download-catalog.js');
const fixture=JSON.parse(readFileSync(new URL('./fixtures/catalog-v1.json',import.meta.url)));
const clone=x=>structuredClone(x),base=fixture.entries[0];let passed=0;
function t(name,fn){fn();passed++;console.log('PASS',name);}
const checked=doc=>C.validate(doc,P.validate(base));
t('exact contracted identity matches',()=>assert.equal(checked(fixture).state,'ok'));
t('old pointer still valid and unknown',()=>{const p=clone(base);delete p.layout;assert.equal(P.validate(p).state,'ok');assert.equal(P.validate(p).pointer.layout,'unknown');assert.equal(C.validate(fixture,P.validate(p)).state,'invalid');});
t('unknown catalog can agree with unknown old pointer',()=>{const d=clone(fixture),p=clone(base);delete p.layout;d.entries[0].layout='unknown';assert.equal(C.validate(d,P.validate(p)).state,'ok');});
t('all supported pointer layouts',()=>{for(const layout of ['unknown','legacy-flat-v1','portable-v1'])assert.equal(P.validate({...base,layout}).pointer.layout,layout);});
t('bad optional pointer layouts rejected',()=>{for(const layout of [null,0,'portable',{},[]])assert.equal(P.validate({...base,layout}).state,'invalid');});
t('empty catalog agrees only with none pointer',()=>{const d={schema:1,channel:'prototype',complete:true,selected_tag:null,entries:[]};assert.equal(C.validate(d,{state:'none'}).state,'none');assert.equal(checked(d).state,'invalid');});
t('all selected identity mismatches reject history only',()=>{const primary=P.validate(base);for(const key of ['version','tag','commit','url','label','sha256','size','layout']){const changed=clone(primary);changed.pointer[key]=key==='size'?1:'different';assert.equal(C.validate(fixture,changed).state,'invalid',key);assert.equal(primary.state,'ok');}});
t('strict prototype validator reused',()=>{for(const key of ['schema','channel','asset','url','commit','sha256','version','tag']){const d=clone(fixture);d.entries[0][key]='invalid';assert.equal(checked(d).state,'invalid',key);}});
t('catalog layout required even when old pointer allows omission',()=>{const d=clone(fixture);delete d.entries[0].layout;assert.equal(checked(d).state,'invalid');});
t('reject bad envelope',()=>{for(const d of [null,[],{}, {...fixture,complete:false},{...fixture,schema:2},{...fixture,channel:'stable'},{...fixture,entries:null},{...fixture,selected_tag:null}])assert.equal(checked(d).state,'invalid');});
t('reject invalid or absent independent pointer',()=>{for(const r of [undefined,{state:'invalid'},{state:'none'}])assert.equal(C.validate(fixture,r).state,'invalid');});
function entry(version){return {...base,version,tag:'v'+version,url:base.url.replace(base.tag,'v'+version)};}
t('descending global build not semver',()=>{const top=entry('0.0.0-proto.9'),low=entry('99.99.99-proto.8');const d={...fixture,selected_tag:top.tag,entries:[top,low]};assert.equal(C.validate(d,P.validate(top)).state,'ok');d.entries.reverse();assert.equal(C.validate(d,P.validate(top)).state,'invalid');});
t('duplicate tags and cross-version build ties rejected',()=>{for(const e of [base,entry('1.2.3-proto.1'),entry('1.2.3-proto.01')])assert.equal(checked({...fixture,entries:[base,e]}).state,'invalid');});
t('selected tag must be present and greatest',()=>{assert.equal(checked({...fixture,selected_tag:'v0.1.0-proto.99'}).state,'invalid');const top=entry('0.1.0-proto.2');assert.equal(checked({...fixture,entries:[top,base]}).state,'invalid');});
t('unsafe size and oversized inventory reject',()=>{const d=clone(fixture);d.entries[0].size=Number.MAX_SAFE_INTEGER+1;assert.equal(checked(d).state,'invalid');assert.equal(checked({...fixture,entries:Array(1001).fill(base)}).state,'invalid');});
// Network checks only use in-process mocked streams, never public services.
globalThis.location={origin:'https://example.test',href:'https://example.test/site/'};
let calls=0;globalThis.fetch=async()=>{calls++;return new Response(JSON.stringify(fixture));};
assert.equal((await C.load('https://example.test/site/downloads.json',P.validate(base))).state,'ok');passed++;
for(const url of ['http://example.test/downloads.json','https://other.test/downloads.json'])assert.equal((await C.load(url,P.validate(base))).state,'invalid');assert.equal(calls,1);passed++;
for(const response of [new Response('bad json'),new Response('{}',{status:503}),new Response('x'.repeat(4194305))]){globalThis.fetch=async()=>response;assert.equal((await C.load('https://example.test/downloads.json',P.validate(base))).state,'invalid');}passed++;
globalThis.fetch=async()=>{throw new Error('offline');};assert.equal((await C.load('https://example.test/downloads.json',P.validate(base))).state,'invalid');passed++;
console.log(passed+' catalog checks passed');
// Minimal element doubles test handler lifecycle without enrolling the module.
class Element {
 constructor(tag){this.tagName=tag;this.children=[];this.disabled=false;this.onclick=null;this.textContent='';this.attributes={};}
 setAttribute(k,v){this.attributes[k]=v;}
 getAttribute(k){return this.attributes[k];}
 appendChild(e){this.children.push(e);}
 replaceChildren(){this.children=[];}
 querySelectorAll(tag){return this.children.flatMap(e=>[...(e.tagName===tag?[e]:[]),...e.querySelectorAll(tag)]);}
}
globalThis.document={createElement:tag=>new Element(tag)};
let assigned=null;globalThis.location.assign=url=>{assigned=url;};
t('catalog renders validated row and clears detached handlers on invalidation',()=>{const target=new Element('div'),primary=P.validate(base);C.render(target,C.validate(fixture,primary));const old=target.querySelectorAll('button')[0];assert.equal(old.disabled,false);old.onclick();assert.equal(assigned,base.url);C.render(target,{state:'invalid'});assert.equal(old.disabled,true);assert.equal(old.onclick,null);assert.equal(target.querySelectorAll('button').length,0);assert.equal(primary.state,'ok');});
t('empty catalog has no handlers and uses a text-only notice',()=>{const target=new Element('div');C.render(target,{state:'none'});assert.equal(target.querySelectorAll('button').length,0);assert.equal(target.children[0].tagName,'p');});
t('every history row accessible label carries exact version and tag',()=>{const target=new Element('div');C.render(target,C.validate(fixture,P.validate(base)));const row=target.querySelectorAll('button')[0];const label=row.getAttribute('aria-label');assert.ok(label.includes(base.version),'version in label');assert.ok(label.includes(base.tag),'tag in label');assert.ok(label.includes('\u2066'+base.version+'\u2069'),'version LTR-isolated');assert.ok(label.startsWith('הורדה חינם '),'full localized visible wording leads the label');});
console.log(passed+' catalog checks passed including renderer lifecycle');
