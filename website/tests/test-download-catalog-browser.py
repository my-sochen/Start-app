from pathlib import Path
import json,subprocess,tempfile,time,urllib.request,websocket,base64,mimetypes,shutil
import argparse
ap=argparse.ArgumentParser();ap.add_argument('--pointer-snapshot',required=True);ap.add_argument('--output',required=True);args=ap.parse_args()
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(args.output);OUT.mkdir(parents=True,exist_ok=True)
pointer=json.loads(Path(args.pointer_snapshot).read_text())
actual_pointer=pointer.copy();catalog=None
profile=tempfile.mkdtemp(prefix='working-ui-');log=open(OUT/'chrome.log','w');p=subprocess.Popen(['google-chrome','--headless=new','--no-sandbox','--disable-gpu','--disable-background-networking','--disable-component-update','--host-resolver-rules=MAP * 127.0.0.1','--remote-debugging-port=9339','--remote-allow-origins=*','--user-data-dir='+profile,'about:blank'],stdout=log,stderr=log)
checks=[];errors=[];requests=[];state='ok'
try:
 for _ in range(80):
  try:tabs=json.load(urllib.request.urlopen('http://127.0.0.1:9339/json'));break
  except Exception:time.sleep(.1)
 ws=websocket.create_connection(tabs[0]['webSocketDebuggerUrl']);count=0;results={}
 def send(m,a={}):
  global count
  count+=1;ws.send(json.dumps({'id':count,'method':m,'params':a}));return count
 def wait(n):
  while n not in results:
   e=json.loads(ws.recv())
   if e.get('method')=='Fetch.requestPaused':
    d=e['params'];url=d['request']['url'];requests.append(url);rel=url.split('/Start-app/',1)[-1].split('?',1)[0].split('#',1)[0];status=200
    if rel=='downloads.json':
     body=json.dumps(catalog).encode() if catalog is not None else b'not found';mime='application/json';status=200 if catalog is not None else 404
    elif rel=='pointer.json':
     v=pointer if state=='ok' else {'schema':1,'channel':'prototype','candidate':None} if state=='none' else {'schema':1,'channel':'prototype','url':'https://untrusted.invalid/fake.zip'}
     body=json.dumps(v).encode();mime='application/json'
     if state=='failure':status=503
    elif 'releases/download/' in url:
     body=b'Navigation intercepted by private test, no binary fetched.';mime='text/plain'
    else:
     f=ROOT/rel
     body=f.read_bytes()if f.exists()and f.is_file()else b'';mime=mimetypes.guess_type(rel)[0]or'application/octet-stream'
    send('Fetch.fulfillRequest',{'requestId':d['requestId'],'responseCode':status,'responseHeaders':[{'name':'Content-Type','value':mime}],'body':base64.b64encode(body).decode()})
   elif e.get('method')=='Runtime.exceptionThrown':errors.append(e['params'])
   elif'id'in e:results[e['id']]=e
  r=results.pop(n)
  if 'error' in r:raise RuntimeError(r)
  return r.get('result',{})
 def js(s):
  r=wait(send('Runtime.evaluate',{'expression':s,'returnByValue':True,'awaitPromise':True}))
  if 'exceptionDetails' in r:raise RuntimeError(r)
  return r.get('result',{}).get('value')
 def check(name,expr):
  value=js(expr);checks.append({'name':name,'pass':bool(value)})
  if not value:raise AssertionError(name)
 def settle():
  try:js('new Promise(r=>setTimeout(r,80))')
  except RuntimeError as e:
   if 'navigated or closed' not in str(e):raise
   js('new Promise(r=>setTimeout(r,80))')
 def nav(path='index.html',width=390,height=1800):
  wait(send('Emulation.setDeviceMetricsOverride',{'width':width,'height':height,'deviceScaleFactor':1,'mobile':False}));wait(send('Page.navigate',{'url':'https://my-sochen.github.io/Start-app/'+path}));js('document.fonts.ready.then(()=>true)');settle()
 def shot(name,width,height,guide=False):
  if guide:
   js('window.scrollTo(0,document.querySelector("#install").getBoundingClientRect().top+scrollY-20)');h=min(height,int(js('Math.ceil(document.querySelector("[data-layout-legacy]:not([hidden]),[data-layout-portable]:not([hidden]),[data-layout-unknown]:not([hidden])").getBoundingClientRect().bottom)+20')))
  else:
   js('window.scrollTo(0,0)');h=min(height,int(js('Math.ceil(document.querySelector("footer").getBoundingClientRect().bottom)+12')))
  image=wait(send('Page.captureScreenshot',{'format':'png','captureBeyondViewport':False}));raw=base64.b64decode(image['data'])
  from PIL import Image
  import io
  im=Image.open(io.BytesIO(raw));im.crop((0,0,width,h)).save(OUT/name)
 wait(send('Page.enable'));wait(send('Page.bringToFront'));wait(send('Runtime.enable'));wait(send('Fetch.enable',{'patterns':[{'urlPattern':'*'}]}))

 # Synthetic catalog entries are isolated test data, never release claims.
 def entry(build,layout='portable-v1'):
  e=actual_pointer.copy();e.update(version='0.0.0-proto.'+str(build),layout=layout)
  e['tag']='v'+e['version'];e['url']='https://github.com/my-sochen/Start-app/releases/download/'+e['tag']+'/StartApp-win-x64.zip';return e
 def inventory(entries):return {'schema':1,'channel':'prototype','complete':True,'selected_tag':entries[0]['tag'] if entries else None,'entries':entries}
 def fixture_banner():
  js('(()=>{let p=document.createElement("p");p.textContent="SYNTHETIC TEST FIXTURES, NOT RELEASE AVAILABILITY";p.style="position:fixed;top:0;left:0;right:0;z-index:999;background:#fff0c0;color:#111;font:12px Arial;padding:6px;direction:ltr";document.body.appendChild(p)})()')
 pointer=entry(9);catalog=inventory([pointer,entry(5,'legacy-flat-v1'),entry(2,'unknown')]);nav();settle()
 check('multiple eligible entries rendered','document.querySelectorAll(".prototype-offer").length===3')
 check('first row no top border, later separators kept','getComputedStyle(document.querySelectorAll(".prototype-offer")[0]).borderTopWidth==="0px" && getComputedStyle(document.querySelectorAll(".prototype-offer")[1]).borderTopWidth==="1px"')
 check('each row labeled with its own version and tag','[...document.querySelectorAll(".prototype-offer")].every(r=>{const v=r.querySelector("bdi").textContent;const a=r.querySelector("button").getAttribute("aria-label");return a&&a.startsWith("הורדה חינם ")&&a.includes(v)&&a.includes("v"+v)})')
 check('primary version visible','document.querySelector("[data-cta-version]").textContent==="0.0.0-proto.9" && !document.querySelector("[data-cta-version]").hidden')
 check('complete hint is publication snapshot','!document.querySelector(".download-all-hint").textContent.includes("לא ארכיון")')
 js('document.querySelector(".download-all").open=true');fixture_banner();shot('fixture-multiple-open.png',390,2000)
 js('window.oldRows=[...document.querySelectorAll(".prototype-offer button")];window.StartAppCatalog.load=()=>Promise.resolve({state:"invalid"});StartAppDownload.init()');settle()
 check('history failure clears old handlers only','window.oldRows.every(b=>b.disabled&&b.onclick===null) && !document.querySelector(".prototype-offer") && !document.querySelector(".download-direct").disabled')
 # A late catalog result from a superseded generation cannot replace newer data.
 nav();js('window.originalLoad=StartAppCatalog.load;window.StartAppCatalog.load=()=>new Promise(r=>window.resolveOld=r);StartAppDownload.init()');settle()
 js('window.StartAppCatalog.load=()=>Promise.resolve({state:"invalid"});StartAppDownload.init()');settle()
 js('window.resolveOld('+json.dumps({'state':'ok','entries':[pointer],'selected_tag':pointer['tag']})+')');settle()
 check('stale inflight catalog cannot overwrite new failure','!document.querySelector(".prototype-offer") && !document.querySelector(".download-direct").disabled')
 # Router swap increments generation and removes references to old document rows.
 nav();js('window.StartAppCatalog.load=()=>new Promise(r=>window.resolveRoute=r);StartAppDownload.init()');settle()
 js('document.querySelectorAll("header > nav a")[0].click()');settle();settle()
 js('window.resolveRoute('+json.dumps({'state':'ok','entries':[pointer],'selected_tag':pointer['tag']})+')');settle()
 check('late catalog cannot mutate routed guide','location.pathname.endsWith("docs.html") && !document.querySelector(".prototype-offer") && !document.querySelector(".doc-download button").disabled')
 check('portable has no legacy verification claim','document.querySelector("[data-layout-legacy-qualification]").hidden')
 check('portable guide branch from pointer only','!document.querySelector("[data-layout-portable]").hidden && document.querySelector("[data-layout-legacy]").hidden && document.querySelector("[data-layout-unknown]").hidden')
 check('unproven package gets design wording, not package claims','!document.querySelector("[data-layout-legacy-unqualified]").hidden && [...document.querySelectorAll("[data-layout-legacy-qualification]")].every(n=>n.hidden)')
 fixture_banner();shot('fixture-portable-guide.png',390,2000,True)
 for layout in ['legacy-flat-v1','unknown']:
  pointer=entry(9,layout);catalog=inventory([pointer]);nav('docs.html');settle()
  selector='[data-layout-legacy]' if layout=='legacy-flat-v1' else '[data-layout-unknown]'
  check(layout+' guide branch', '!document.querySelector('+json.dumps(selector)+').hidden')
  check(layout+' never inherits other-package runtime claim','[...document.querySelectorAll("[data-layout-legacy-qualification]")].every(n=>n.hidden)')
  check(layout+' sees design wording not package claims','!document.querySelector("[data-layout-legacy-unqualified]").hidden')
  fixture_banner();shot('fixture-'+layout+'-guide.png',390,2000,True)
 pointer=actual_pointer.copy();catalog=None;nav();settle()
 check('old pointer missing catalog keeps current singleton','document.querySelectorAll(".prototype-offer").length===1 && !document.querySelector(".download-direct").disabled && document.querySelector(".download-all-hint").textContent.includes("לא ארכיון")')
 nav('docs.html');check('old missing layout uses unknown guide','!document.querySelector("[data-layout-unknown]").hidden && document.querySelector("[data-layout-legacy]").hidden')
 check('exact documented old package retains only its own runtime qualification','[...document.querySelectorAll("[data-layout-legacy-qualification]")].every(n=>!n.hidden)')
 check('documented package hides design fallback wording','document.querySelector("[data-layout-legacy-unqualified]").hidden')
 pointer=entry(9);catalog=inventory([entry(9,'legacy-flat-v1')]);nav();settle()
 check('identity layout conflict isolated from primary','!document.querySelector(".prototype-offer") && !document.querySelector(".download-direct").disabled')
 pointer=entry(9);catalog=inventory([pointer]);nav();settle()
 js('window.old=document.querySelector(".prototype-offer button")');state='none';js('StartAppDownload.init()');settle()
 check('withdrawal invalidates historical controls and primary','window.old.disabled && window.old.onclick===null && document.querySelector(".download-direct").disabled && !document.querySelector(".prototype-offer") && document.querySelector("[data-cta-version]").hidden')
 # Empty complete snapshot is distinct from unavailable and never clickable.
 state='none';catalog=inventory([]);nav();settle()
 check('complete empty inventory','!document.querySelector(".prototype-offer") && document.querySelector(".download-direct").disabled && !document.querySelector(".download-all-hint").textContent.includes("לא ארכיון")')
 state='ok';pointer=entry(9);catalog=inventory([pointer]);nav();settle()
 js('window.resolvePointer=null;window.originalPointer=StartAppPointer.load;StartAppPointer.load=()=>new Promise(r=>window.resolvePointer=r);StartAppDownload.init()');settle()
 check('reinit clears visible version until new pointer','document.querySelector(".download-direct").disabled && document.querySelector("[data-cta-version]").hidden && !document.querySelector(".prototype-offer")')
 js('StartAppPointer.load=()=>Promise.resolve({state:"invalid"});StartAppDownload.init()');settle()
 js('window.resolvePointer('+json.dumps({'state':'ok','pointer':{k:pointer[k] for k in ['version','tag','commit','url','label','size','sha256','layout']}})+')');settle()
 check('late pointer cannot overwrite newer invalid state','document.querySelector(".download-direct").disabled && !document.querySelector(".prototype-offer")')
 state='ok';pointer=entry(9);catalog=inventory([pointer,entry(2)]);nav();settle();js('document.querySelectorAll(".prototype-offer button")[1].click()');settle()
 check('historical click exact validated row URL','location.href==='+json.dumps(entry(2)['url']))
 check('no runtime exceptions', 'true' if not errors else 'false')
 print('PASS',len(checks),'integrated browser checks; exceptions',len(errors))
finally:
 (OUT/'checks.json').write_text(json.dumps({'checks':checks,'exceptions':errors,'synthetic_fixture_notice':'Synthetic versions/layout branches, not real release availability','request_count':len(requests)},ensure_ascii=False,indent=2));p.terminate();p.wait(timeout=10);log.close();shutil.rmtree(profile,ignore_errors=True)
