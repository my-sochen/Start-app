// Local HTTPS fixtures only. No real release download and no hosted preview.
import puppeteer from 'puppeteer-core';
import {createServer} from 'node:https';
import {execFileSync} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'../..');
const out=process.env.VERSION_SHOTS||'/tmp/versioned-guide-evidence';fs.mkdirSync(out,{recursive:true});
const certs=path.join(out,'certs');fs.mkdirSync(certs,{recursive:true});
execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-keyout',certs+'/key.pem','-out',certs+'/cert.pem','-days','2','-subj','/CN=127.0.0.1'],{stdio:'pipe'});
const origin='https://127.0.0.1:8782';let mode='ok',external=0,checks=0;
const check=(x,msg)=>{assert.ok(x,msg);checks++;};
const template=JSON.parse(fs.readFileSync(root+'/tests/dom-harness/fixtures/ok.json'));
const entry=n=>({...template,version:'0.1.0-proto.'+n,tag:'v0.1.0-proto.'+n,url:'https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.'+n+'/StartApp-win-x64.zip',layout:n===1?'legacy-flat-v1':'portable-v1'});
const pointer=entry(2),catalog={schema:1,channel:'prototype',complete:true,selected_tag:pointer.tag,entries:[pointer,entry(1)]};
const server=createServer({key:fs.readFileSync(certs+'/key.pem'),cert:fs.readFileSync(certs+'/cert.pem')},(req,res)=>{
 const p=decodeURIComponent(req.url.split('?')[0]).replace(/^\/Start-app/,'');
 res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.json':'application/json','.png':'image/png','.svg':'image/svg+xml','.ttf':'font/ttf'})[path.extname(p)]||'application/octet-stream');
 if(p==='/pointer.json'){res.end(JSON.stringify(pointer));return;}
 if(p==='/downloads.json'){if(mode==='missing'){res.statusCode=404;res.end('{}');return;}res.end(JSON.stringify(mode==='absent'?{...catalog,entries:[pointer]}:catalog));return;}
 const f=path.resolve(root,'.'+p);if(!f.startsWith(root+path.sep)){res.statusCode=403;res.end();return;}try{res.end(fs.readFileSync(f));}catch{res.statusCode=404;res.end('Not found');}
}).listen(8782,'127.0.0.1');
const browser=await puppeteer.launch({executablePath:'/usr/bin/google-chrome',headless:true,args:['--no-sandbox','--ignore-certificate-errors']});
const langs=['he','en','ru','zh-Hans','ja','fr','es','de'];const errors=[];
async function pageFor(url,w=1440){const p=await browser.newPage();await p.setViewport({width:w,height:w===390?844:1000});await p.setRequestInterception(true);p.on('request',r=>{if(r.url().startsWith(origin)||r.url().startsWith('data:'))r.continue();else{external++;r.abort();}});p.on('pageerror',e=>errors.push(e.message));await p.goto(origin+'/Start-app/'+url,{waitUntil:'networkidle0'});await p.evaluate(async()=>{await document.fonts.ready;await Promise.all([...document.images].map(i=>{i.loading='eager';return i.decode().catch(()=>{});}));});return p;}
async function mark(p){await p.evaluate(()=>{const b=document.createElement('p');b.textContent='LOCAL REVIEW / FIXTURE AVAILABILITY ONLY / original app UI screenshots';b.style.cssText='position:fixed;bottom:0;left:0;right:0;margin:0;padding:5px;background:#fff4ca;color:#322900;z-index:9999;font:12px Arial;text-align:center';document.body.append(b);});}
try{
 for(const n of [1,2])for(const lang of langs){
  const folder=lang==='he'?'':lang+'/';const url=(n===1?'proto1/':'')+folder+'docs.html';const p=await pageFor(url);
  check(await p.$$eval('.doc-download a.button',a=>a.length===1&&a[0].getAttribute('href')===document.querySelector('.doc-version-download a').getAttribute('href')&&a[0].textContent.trim().length>0),`${n}/${lang} prominent existing-style button retains exact version and locale target`);
  const state=await p.evaluate(()=>({overflow:document.documentElement.scrollWidth>innerWidth+1,lang:document.documentElement.lang,version:document.querySelector('.version-switch summary')?.textContent,ctas:document.querySelectorAll('[data-download-cta]').length,download:document.querySelector('.doc-version-download a')?.href,images:[...document.querySelectorAll('img')].map(i=>({src:i.src,alt:i.alt,ok:i.complete&&i.naturalWidth>0})),switches:[...document.querySelectorAll('.language-switch a')].map(a=>a.href)}));
  check(state.lang===lang&&!state.overflow,`${n}/${lang} layout and language`);check(state.ctas===0&&state.version.includes('0.1.0-proto.'+n),`${n}/${lang} fixed guide version and no download control`);
  check(new URL(state.download).pathname==='/Start-app/'+folder+'index.html'&&new URL(state.download).search==='?version=v0.1.0-proto.'+n,`${n}/${lang} correct locale version link`);
  check(state.images.every(i=>i.alt&&i.ok),`${n}/${lang} loaded images and alt`);check(state.switches.length===8&&state.switches.every(s=>s.includes('/proto1/')===(n===1)),`${n}/${lang} language switch preserves version`);
  const broken=await p.evaluate(async()=>{const urls=[...new Set([...document.querySelectorAll('a[href],link[href],script[src],img[src]')].map(e=>e.href||e.src).filter(s=>s.startsWith(location.origin)))];return (await Promise.all(urls.map(async u=>[u,(await fetch(u)).status]))).filter(x=>x[1]!==200)});check(!broken.length,`${n}/${lang} no broken local resource/link: ${JSON.stringify(broken)}`);
  await mark(p);await p.screenshot({path:out+`/proto${n}-${lang}-desktop.png`,fullPage:true});
  await p.setViewport({width:390,height:844});check(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${n}/${lang} mobile no overflow`);await p.screenshot({path:out+`/proto${n}-${lang}-mobile.png`,fullPage:true});
  // Version dropdown uses real navigation. Back restores the original version.
  await p.$eval('.version-switch',e=>e.open=true);await Promise.all([p.waitForNavigation({waitUntil:'networkidle0'}),p.click('.version-switch a:not([aria-current])')]);check((await p.url()).includes('/proto1/')===(n===2),`${n}/${lang} version navigation`);await p.goBack({waitUntil:'networkidle0'});check(p.url().endsWith('/'+url),`${n}/${lang} history back`);await p.goForward({waitUntil:'networkidle0'});check((await p.url()).includes('/proto1/')===(n===2),`${n}/${lang} history forward`);await p.goBack({waitUntil:'networkidle0'});
  await Promise.all([p.waitForNavigation({waitUntil:'networkidle0'}),p.click('.doc-download a.button')]);await p.waitForSelector('.version-target');
  check(await p.$eval('.version-target',(e,n)=>e.dataset.tag==='v0.1.0-proto.'+n,n),`${n}/${lang} requested row highlighted`);check(await p.$eval('.version-target',e=>{const r=e.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight}),`${n}/${lang} target visible after scroll`);
  await mark(p);await p.screenshot({path:out+`/proto${n}-${lang}-highlight.png`});await p.close();
 }
 for(const m of ['absent','missing']){mode=m;const p=await pageFor('en/index.html?version=v0.1.0-proto.1#download');await p.waitForSelector('[data-version-request-note]:not([hidden])');check(await p.$eval('[data-version-request-note]',e=>e.textContent.includes('not currently offered')),m+' truthful note');check(await p.$$eval('.version-target',a=>a.length===0),m+' no substituted highlight');await mark(p);await p.screenshot({path:out+'/'+m+'-version.png'});await p.close();}
 mode='ok';const p=await pageFor('proto1/en/docs.html');await p.$eval('.language-switch',e=>e.open=true);await Promise.all([p.waitForNavigation({waitUntil:'networkidle0'}),p.click('.language-switch a[lang="de"]')]);check(p.url().endsWith('/proto1/de/docs.html'),'archive cross-language navigation');await p.close();
 check(external===0,'no external requests / no real binaries fetched');check(errors.length===0,'no JS errors: '+errors.join(';'));
 fs.writeFileSync(out+'/RESULT.json',JSON.stringify({checks,external,errors,languages:langs,versions:[1,2],fixtureOnly:true},null,2));console.log(checks+' versioned-guide browser checks passed');
}finally{await browser.close();server.close();}
