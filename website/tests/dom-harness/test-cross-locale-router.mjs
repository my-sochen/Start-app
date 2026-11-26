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
const origin='https://127.0.0.1:8784';let mode='ok',external=0,checks=0;
const check=(x,msg)=>{assert.ok(x,msg);checks++;};
const template=JSON.parse(fs.readFileSync(root+'/tests/dom-harness/fixtures/ok.json'));
const entry=n=>({...template,version:'0.1.0-proto.'+n,tag:'v0.1.0-proto.'+n,url:'https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.'+n+'/StartApp-win-x64.zip',layout:n===1?'legacy-flat-v1':'portable-v1'});
const pointer=entry(2),catalog={schema:1,channel:'prototype',complete:true,selected_tag:pointer.tag,entries:[pointer,entry(1)]};
const server=createServer({key:fs.readFileSync(certs+'/key.pem'),cert:fs.readFileSync(certs+'/cert.pem')},(req,res)=>{
 let p=decodeURIComponent(req.url.split('?')[0]).replace(/^\/Start-app/,'');if(p.endsWith('/'))p+='index.html';if(!path.extname(p))p+='.html';
 res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.json':'application/json','.png':'image/png','.svg':'image/svg+xml','.ttf':'font/ttf'})[path.extname(p)]||'application/octet-stream');
 if(p==='/pointer.json'){res.end(JSON.stringify(pointer));return;}
 if(p==='/downloads.json'){if(mode==='missing'){res.statusCode=404;res.end('{}');return;}res.end(JSON.stringify(mode==='absent'?{...catalog,entries:[pointer]}:catalog));return;}
 const f=path.resolve(root,'.'+p);if(!f.startsWith(root+path.sep)){res.statusCode=403;res.end();return;}try{res.end(fs.readFileSync(f));}catch{res.statusCode=404;res.end('Not found');}
}).listen(8784,'127.0.0.1');
const browser=await puppeteer.launch({executablePath:'/usr/bin/google-chrome',headless:true,args:['--no-sandbox','--ignore-certificate-errors']});
const langs=['he','en','ru','zh-Hans','ja','fr','es','de'];const errors=[];
async function pageFor(url,w=1440){const p=await browser.newPage();await p.setViewport({width:w,height:w===390?844:1000});await p.setRequestInterception(true);p.on('request',r=>{if(r.url().startsWith(origin)||r.url().startsWith('data:'))r.continue();else{external++;r.abort();}});p.on('pageerror',e=>errors.push(e.message));await p.goto(origin+'/Start-app/'+url,{waitUntil:'networkidle0'});await p.evaluate(async()=>{await document.fonts.ready;await Promise.all([...document.images].map(i=>{i.loading='eager';return i.decode().catch(()=>{});}));});return p;}
async function mark(p){await p.evaluate(()=>{const b=document.createElement('p');b.textContent='LOCAL REVIEW / FIXTURE AVAILABILITY ONLY / original app UI screenshots';b.style.cssText='position:fixed;bottom:0;left:0;right:0;margin:0;padding:5px;background:#fff4ca;color:#322900;z-index:9999;font:12px Arial;text-align:center';document.body.append(b);});}

async function clickTo(p,selector,condition){await p.click(selector);await p.waitForFunction(condition);await p.waitForNetworkIdle({idleTime:50});}
try{
 for(const entry of ['docs.html','proto1/en/docs.html'])for(const width of [390,1440]){
  const p=await pageFor(entry,width);await p.evaluate(()=>window.__documentProbe='retained');
  for(const lang of langs){
   await p.$eval('.language-switch',e=>e.open=true);
   await clickTo(p,`.language-switch a[lang="${lang}"]`,new Function(`return document.documentElement.lang===${JSON.stringify(lang)}`));
   check(await p.evaluate(()=>window.__documentProbe==='retained'),'language change retains original document');
   check(await p.evaluate(()=>document.documentElement.dir===(document.documentElement.lang==='he'?'rtl':'ltr')),'destination text direction');
   check(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'no locale overflow');
   check(await p.$$eval('img',a=>a.every(i=>i.complete&&i.naturalWidth>0)),'destination images predecoded');
   check(await p.evaluate(()=>!document.querySelector('#search-status').textContent.includes('פרקי מדריך')||document.documentElement.lang==='he'),'search count uses current language');
   await p.$eval('.version-switch',e=>e.open=true);const before=p.url();
   await clickTo(p,'.version-switch a:not([aria-current])',new Function(`return location.href!==${JSON.stringify(before)}`));
   check(await p.evaluate(()=>window.__documentProbe==='retained'),'version switch retains document');
   check(await p.evaluate(()=>new URL(document.querySelector('.brand img').src).pathname==='/Start-app/icons/header-ufo.svg'),'archive image root');
   await clickTo(p,'header > nav a:last-child',()=>document.body.dataset.page==='index.html');
   check(await p.evaluate(()=>window.__documentProbe==='retained'&&scrollY===0&&!location.search&&!location.hash),'clean Download opens top without reload');
   await p.waitForSelector('[data-download-cta]:not([disabled])');
   check(await p.evaluate(()=>!document.querySelector('[data-download-cta]').textContent.includes('הורדה')||document.documentElement.lang==='he'),'download initializer current language');
   await clickTo(p,'header > nav a:first-child',()=>document.body.dataset.page==='docs.html');
   check(await p.evaluate(()=>window.__documentProbe==='retained'),'guide return without reload');
   if(lang==='en'||lang==='he')await p.screenshot({path:out+`/router-${entry.replaceAll('/','-')}-${lang}-${width}.png`});
  }
  await p.close();
 }
 // Deep version links, history and relative media after cross-locale/archive routes.
 let p=await pageFor('proto1/ru/docs.html',390);await p.evaluate(()=>window.__documentProbe='retained');
 await p.click('.doc-download a.button');await p.waitForSelector('.version-target');
 check(await p.evaluate(()=>window.__documentProbe==='retained'&&document.querySelector('.version-target').dataset.tag==='v0.1.0-proto.1'),'version deep link swaps to correct home row');
 await p.goBack();await p.waitForFunction(()=>location.pathname.includes('proto1'));check(await p.evaluate(()=>window.__documentProbe==='retained'&&document.documentElement.lang==='ru'),'back restores archive/locale');
 await p.goForward();await p.waitForSelector('.version-target');check(await p.evaluate(()=>window.__documentProbe==='retained'),'forward version link no reload');await p.close();
 p=await pageFor('en/docs.html#privacy');check(await p.evaluate(()=>!document.querySelector('#privacy').hidden),'direct guide hash remains available');await p.close();
 p=await browser.newPage();await p.setJavaScriptEnabled(false);await p.goto(origin+'/Start-app/proto1/de/docs.html');await Promise.all([p.waitForNavigation(),p.click('header > nav a:last-child')]);check(p.url().endsWith('/de/index.html'),'no JS archived Download native fallback');await p.close();
 // Same-document URL variants and a pending cross-language navigation.
 p=await pageFor('en/',390);await p.evaluate(()=>window.__documentProbe='retained');
 await p.evaluate(()=>{const a=document.createElement('a');a.id='pretty-route';a.href='../de/docs/';a.textContent='route probe';document.body.append(a)});
 await clickTo(p,'#pretty-route',()=>document.documentElement.lang==='de'&&document.body.dataset.page==='docs.html');
 check(await p.evaluate(()=>window.__documentProbe==='retained'),'pretty trailing-slash guide routes without reload');await p.close();
 p=await pageFor('proto1/de/docs.html',390);await p.evaluate(()=>window.__documentProbe='retained');
 await p.click('.doc-download a.button');await p.waitForSelector('.version-target');
 await p.evaluate(()=>window.scrollTo({top:0,behavior:'instant'}));await new Promise(r=>setTimeout(r,180));
 await clickTo(p,'header > nav a:first-child',()=>document.body.dataset.page==='docs.html');
 await p.goBack();await p.waitForSelector('.version-target');await p.waitForNetworkIdle({idleTime:50});
 check(await p.evaluate(()=>scrollY===0&&window.__documentProbe==='retained'),'history restores chosen home position instead of forcing version-row jump');await p.close();
 check(external===0,'no external requests');check(errors.length===0,'no JS errors: '+errors.join(';'));console.log(checks+' cross-locale router checks passed');
}finally{await browser.close();server.close()}
