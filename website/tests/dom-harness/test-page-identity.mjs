// Source-only regression: first paint, disabled/delayed JS and Pretty URL aliases.
import puppeteer from 'puppeteer-core';
import {createServer} from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'../..'),out=process.env.IDENTITY_SHOTS||'/tmp/page-identity-evidence';fs.mkdirSync(out,{recursive:true});let checks=0;
const check=(v,m)=>{assert.ok(v,m);checks++};
const server=createServer((req,res)=>{let p=decodeURIComponent(req.url.split('?')[0]).replace(/^\/Start-app/,'');if(p.endsWith('/'))p+='index.html';if(!path.extname(p))p+='.html';const f=path.resolve(root,'.'+p);if(!f.startsWith(root+path.sep)){res.writeHead(403).end();return;}res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml','.ttf':'font/ttf','.json':'application/json'})[path.extname(p)]||'application/octet-stream');try{res.end(fs.readFileSync(f))}catch{res.writeHead(404).end()}}).listen(8783,'127.0.0.1');
const browser=await puppeteer.launch({executablePath:'/usr/bin/google-chrome',headless:true,args:['--no-sandbox']});
const state=p=>p.evaluate(()=>({page:document.body.dataset.page||'',background:getComputedStyle(document.body,'::before').backgroundImage,hero:document.querySelector('.hero-art')?getComputedStyle(document.querySelector('.hero-art')).display:null}));
try{
 let p;
 for(const lang of ['','en/','ru/','zh-Hans/','ja/','fr/','es/','de/'])for(const w of [1440,390])for(const js of [false,true])for(const page of ['index.html','docs.html','index','docs','proto1/docs.html']){
  p=await browser.newPage();await p.setViewport({width:w,height:w===390?844:1000});await p.setJavaScriptEnabled(js);await p.goto('http://127.0.0.1:8783/Start-app/'+(page.startsWith('proto1/')?'proto1/'+lang+'docs.html':lang+page),{waitUntil:'load'});const logo=await p.$eval('.brand img',async e=>{await e.decode();return e.naturalWidth>0&&e.getAttribute('alt')===''&&e.width===29&&e.height===29});check(logo,'shared approved header image loads');check(await p.$eval('link[rel=icon]',e=>e.href.endsWith('favicon.svg?v=blue-trace')),'versioned shared favicon');const s=await state(p);check(s.page===(page.includes('docs')?'docs.html':'index.html'),`${lang}${page}/${w}/js=${js} identity`);check(s.background!=='none',`${lang}${page}/${w}/js=${js} decorative background`);
  if(!js&&['','en/'].includes(lang)&&page.endsWith('.html')){await p.screenshot({path:out+`/logo-${lang.replaceAll('/','-')}${page.replaceAll('/','-')}-${w}.png`});}
  if(js&&page==='docs.html'){
   const nav=await p.$eval('header > nav a:last-child',e=>e.href);check(!new URL(nav).hash&&!new URL(nav).search,'top Download has no jump parameters');
   await p.evaluate(()=>{window.__sameDocumentNavProbe=true;window.scrollTo(0,600)});await p.click('header > nav a:last-child');await p.waitForFunction(()=>document.body.dataset.page==='index.html');
   check(await p.evaluate(()=>window.__sameDocumentNavProbe&&window.scrollY===0),'top Download opens home at top without full reload');
  }
  await p.close();
 }
 // Hold the router request pending: initial layout must already be final.
 p=await browser.newPage();await p.setViewport({width:390,height:844});await p.setRequestInterception(true);let held;
 p.on('request',r=>{if(r.url().endsWith('/site-nav.js'))held=r;else r.continue()});const loading=p.goto('http://127.0.0.1:8783/Start-app/index.html',{waitUntil:'load'});await p.waitForSelector('main');check((await state(p)).page==='index.html','delayed router initial home marker');await p.screenshot({path:out+'/after-home-delayed-router.png'});check(!!held,'router request held');await held.continue();await loading;check((await state(p)).page==='index.html','delayed router preserves home marker');await p.close();
 console.log(checks+' page-identity checks passed');
}finally{await browser.close();server.close()}
