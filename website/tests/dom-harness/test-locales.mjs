import puppeteer from 'puppeteer-core';
import {execFileSync} from 'node:child_process';
import {createServer} from 'node:https';
import fs from 'node:fs';
import path from 'node:path';
const ROOT=path.resolve(import.meta.dirname,'../..');
const OUT=process.env.LOCALE_SHOTS||path.join(ROOT,'tests/dom-harness/screenshots/locales');fs.mkdirSync(OUT,{recursive:true});
const certs=path.join(ROOT,'tests/dom-harness/certs');
if(!fs.existsSync(certs+'/cert.pem')){fs.mkdirSync(certs,{recursive:true});execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-keyout',certs+'/key.pem','-out',certs+'/cert.pem','-days','2','-subj','/CN=127.0.0.1','-addext','subjectAltName=IP:127.0.0.1']);}
const BASE='https://127.0.0.1:8778';let state='ok';let external=0;let checks=0;
const assert=(yes,name)=>{if(!yes)throw new Error(name);checks++;console.log('PASS',name)};
const server=createServer({key:fs.readFileSync(ROOT+'/tests/dom-harness/certs/key.pem'),cert:fs.readFileSync(ROOT+'/tests/dom-harness/certs/cert.pem')},(req,res)=>{let p=decodeURIComponent(req.url.split('?')[0]);p=p.replace(/^\/Start-app/,'');if(p==='/')p='/index.html';if(p==='/pointer.json'){if(state==='missing'){res.statusCode=404;res.end('missing');return;}res.setHeader('Content-Type','application/json');res.end(fs.readFileSync(ROOT+'/tests/dom-harness/fixtures/'+state+'.json'));return;}const f=path.join(ROOT,p);res.setHeader('Content-Type',({'.html':'text/html','.css':'text/css','.js':'text/javascript','.json':'application/json','.ttf':'font/ttf','.png':'image/png','.svg':'image/svg+xml'})[path.extname(f)]||'application/octet-stream');try{res.end(fs.readFileSync(f))}catch{res.statusCode=404;res.end('Not found')}}).listen(8778,'127.0.0.1');
const browser=await puppeteer.launch({executablePath:'/usr/bin/google-chrome',headless:true,args:['--no-sandbox','--ignore-certificate-errors']});
const langs=process.env.ONLY_LOCALE?process.env.ONLY_LOCALE.split(','):['he','en','ru','zh-Hans','ja','fr','es','de'];
const sleep=n=>new Promise(r=>setTimeout(r,n));
async function pageFor(lang,name,w=390,js=true){const page=await browser.newPage();await page.setViewport({width:w,height:w===390?844:900});await page.setJavaScriptEnabled(js);await page.setRequestInterception(true);page.on('request',r=>{if(r.url().startsWith(BASE)||r.url().startsWith('data:'))r.continue();else{external++;r.abort()}});const folder=lang==='he'?'':lang+'/';await page.goto(BASE+'/Start-app/'+folder+name+'.html',{waitUntil:'networkidle0'});if(js){await page.waitForFunction(()=>{const b=document.querySelector('[data-download-cta]');return b&&(!b.disabled||!document.getElementById(b.getAttribute('aria-describedby')).textContent.includes('…'))});}await page.evaluate(()=>document.fonts.ready);return page;}
async function label(page){await page.evaluate(()=>{const b=document.createElement('div');b.id='fixture-label';b.textContent='LOCAL FIXTURE / DUMMY version + hash - NOT live release availability';b.style.cssText='position:fixed;left:0;right:0;bottom:0;z-index:999;background:#b00020;color:#fff;font:bold 12px Arial;padding:7px;text-align:center;direction:ltr';document.body.append(b)})}
try{
for(const lang of langs){
 state='ok';
 for(const name of ['index','docs'])for(const w of [320,390,1440]){
  const page=await pageFor(lang,name,w);const s=await page.evaluate(()=>({lang:document.documentElement.lang,dir:document.documentElement.dir,overflow:document.documentElement.scrollWidth>innerWidth+1,enabled:!document.querySelector('[data-download-cta]').disabled,links:document.querySelectorAll('.language-switch a').length,hebrew:[...document.querySelectorAll('main p,main h1,main h2,main h3,main summary,main button')].some(e=>/[\u0590-\u05ff]/.test(e.textContent)),brand:document.querySelector('.brand').textContent.trim()}));
  assert(s.lang===lang&&s.dir===(lang==='he'?'rtl':'ltr'),`${lang} ${name} ${w} language/direction`);assert(!s.overflow,`${lang} ${name} ${w} no overflow`);assert(s.enabled&&s.links===8&&s.brand==='Start-app',`${lang} ${name} ${w} gated CTA and language/brand`);if(lang!=='he')assert(!s.hebrew,`${lang} ${name} ${w} no residual Hebrew page copy`);
  if(w!==320){await label(page);await page.screenshot({path:`${OUT}/${lang}-${name}-${w}.png`});if(name==='index'){await page.$eval('details.verify',e=>e.open=true);await page.screenshot({path:`${OUT}/${lang}-expanded-${w}.png`})}else{await page.$eval('#install',e=>window.scrollTo({top:scrollY+e.getBoundingClientRect().top-30,behavior:'instant'}));await page.screenshot({path:`${OUT}/${lang}-install-${w}.png`})}}
  if(name==='docs'&&w===390){await page.evaluate(()=>{const el=document.querySelector('#doc-search');el.value=document.querySelector('#privacy h2').textContent;el.dispatchEvent(new Event('input'))});console.log('SEARCH',lang,await page.evaluate(()=>({input:document.querySelector('#doc-search').value,hidden:document.querySelector('#privacy').hidden,head:document.querySelector('#privacy h2').textContent,status:document.querySelector('#search-status').textContent,shown:[...document.querySelectorAll('[data-doc]')].filter(x=>!x.hidden).length})));assert(await page.evaluate(()=>!document.querySelector('#privacy').hidden&&[...document.querySelectorAll('[data-doc]')].some(x=>x.hidden)),lang+' localized search');}
  await page.close();
 }
 for(const st of ['none','invalid','missing']){state=st;for(const name of ['index','docs']){const page=await pageFor(lang,name);assert(await page.evaluate(()=>{const b=document.querySelector('[data-download-cta]');return b.disabled&&!b.onclick&&document.querySelector('details.verify').hidden}),`${lang} ${name} ${st} fails closed`);await page.close()}}
 state='ok';const p=await pageFor(lang,'index');await p.evaluate(()=>{window.__localeMarker=1});await p.click('header > nav a[href="docs.html"]');await p.waitForFunction(()=>location.pathname.endsWith('docs.html'));await sleep(200);assert(await p.evaluate(()=>window.__localeMarker===1&&document.documentElement.lang===document.querySelector('.language-switch a[aria-current]').lang&&!document.querySelector('[data-download-cta]').disabled),lang+' same-locale SPA retains language and CTA');assert(await p.evaluate(()=>[...document.querySelectorAll('.language-switch a')].every(a=>new URL(a.href).pathname.endsWith('/docs.html'))),lang+' SPA language links retain guide destination');
 const expectedHead=async(name)=>{const folder=lang==='he'?'':lang+'/';return await p.evaluate(async(url)=>{const doc=new DOMParser().parseFromString(await(await fetch(url)).text(),'text/html');return [...doc.head.querySelectorAll('link[rel="alternate"][hreflang]')].map(a=>[a.hreflang,a.href]);},BASE+'/Start-app/'+folder+name+'.html')};
 const currentHead=()=>p.evaluate(()=>[...document.head.querySelectorAll('link[rel="alternate"][hreflang]')].map(a=>[a.hreflang,a.href]));
 const guideHead=await expectedHead('docs'),homeHead=await expectedHead('index');
 assert(guideHead.length===9&&JSON.stringify(await currentHead())===JSON.stringify(guideHead),lang+' home-to-guide SPA matches all 9 direct hreflang alternates');
 await p.evaluate(()=>history.back());await p.waitForFunction(()=>!location.pathname.endsWith('docs.html'));await sleep(200);
 assert(JSON.stringify(await currentHead())===JSON.stringify(homeHead),lang+' back SPA restores all home alternates');
 await p.evaluate(()=>history.forward());await p.waitForFunction(()=>location.pathname.endsWith('docs.html'));await sleep(200);
 assert(JSON.stringify(await currentHead())===JSON.stringify(guideHead),lang+' forward SPA restores all guide alternates');
 await p.click('header a.brand');await p.waitForFunction(()=>!location.pathname.endsWith('docs.html'));await sleep(200);
 assert(JSON.stringify(await currentHead())===JSON.stringify(homeHead),lang+' guide-to-home SPA restores all home alternates');
 await p.close();
 const n=await pageFor(lang,'index',390,false);assert(await n.evaluate(()=>document.querySelector('[data-download-cta]').disabled&&[...document.querySelectorAll('.language-switch a')].every(x=>!!x.getAttribute('href'))),lang+' no-JS stays gated, language links real');await n.close();
}
// Language switch full navigation must keep guide selection and resolved pointer root.
const cross=await pageFor('he','docs');await cross.$eval('.language-switch',e=>e.open=true);await Promise.all([cross.waitForNavigation({waitUntil:'networkidle0'}),cross.click('.language-switch a[lang="de"]')]);assert(await cross.evaluate(()=>document.documentElement.lang==='de'&&location.pathname.endsWith('/de/docs.html')&&!document.querySelector('[data-download-cta]').disabled),'Hebrew guide to German guide language switch');await cross.close();
assert(external===0,'no external requests made, ZIP not fetched');fs.writeFileSync(OUT+'/locale-test-result-'+langs.join('-')+'.json',JSON.stringify({checks,external,localFixture:true,languages:langs},null,2));console.log('ALL',checks,'LOCALE CHECKS PASSED');
}finally{await browser.close();server.close()}
