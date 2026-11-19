from pathlib import Path
import json,subprocess,tempfile,time,urllib.request,websocket,base64,mimetypes,shutil
import argparse
ap=argparse.ArgumentParser();ap.add_argument('--pointer-snapshot',required=True);ap.add_argument('--output',required=True);args=ap.parse_args()
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(args.output);OUT.mkdir(parents=True,exist_ok=True)
pointer=json.loads(Path(args.pointer_snapshot).read_text())
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
    if rel=='pointer.json':
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
   js('window.scrollTo(0,document.querySelector("#install").getBoundingClientRect().top+scrollY-20)');h=min(height,int(js('Math.ceil(document.querySelector(".doc-download").getBoundingClientRect().bottom)+20')))
  else:
   js('window.scrollTo(0,0)');h=min(height,int(js('Math.ceil(document.body.getBoundingClientRect().bottom)+12')))
  image=wait(send('Page.captureScreenshot',{'format':'png','captureBeyondViewport':False}));raw=base64.b64decode(image['data'])
  from PIL import Image
  import io
  im=Image.open(io.BytesIO(raw));im.crop((0,0,width,h)).save(OUT/name)
 wait(send('Page.enable'));wait(send('Page.bringToFront'));wait(send('Runtime.enable'));wait(send('Fetch.enable',{'patterns':[{'urlPattern':'*'}]}))
 for width,height,label in [(1100,1800,'desktop'),(390,2000,'mobile'),(320,2100,'narrow')]:
  state='ok';nav(width=width,height=height)
  check(label+' real component no raster', '!document.querySelector(".hero-download img") && !!document.querySelector("button.download-direct")')
  check(label+' no overflow','document.documentElement.scrollWidth<=innerWidth')
  check(label+' direct valid','!document.querySelector(".download-direct").disabled && typeof document.querySelector(".download-direct").onclick === "function"')
  check(label+' free-download wording with actual version','document.querySelector("[data-cta-label]").textContent==="הורדה חינם" && document.querySelector("[data-cta-version]").textContent==='+json.dumps(pointer['version']))
  check(label+' offer accessible label has wording, own version and tag','document.querySelector(".prototype-offer button").getAttribute("aria-label").startsWith("הורדה חינם ") && document.querySelector(".prototype-offer button").getAttribute("aria-label").includes("'+pointer['version']+'") && document.querySelector(".prototype-offer button").getAttribute("aria-label").includes("'+pointer['tag']+'")')
  check(label+' first row drops double divider, heading rule kept','getComputedStyle(document.querySelector(".prototype-offer")).borderTopWidth==="0px" && getComputedStyle(document.querySelector(".download-inventory h4")).borderBottomWidth==="1px"')
  check(label+' regular boundary below Windows row','getComputedStyle(document.querySelector(".download-inventory .download-platform")).borderBottomWidth==="1px" && document.querySelector(".download-inventory .download-platform + p").textContent.length>0')
  check(label+' regular heading removed, Windows kept','document.querySelectorAll(".download-inventory h4").length===1 && !!document.getElementById("windows-download-title") && document.querySelector(".download-platform bdi").textContent==="Windows"')
  if label=='mobile':
   v=js('StartAppDownload.init();document.querySelector("[data-prototype-offers] p").textContent');checks.append({'name':'mobile loading never claims unavailable','pass':v=='בודק זמינות הורדה…'})
   if v!='בודק זמינות הורדה…':raise AssertionError('loading state')
   settle()
  check(label+' one actual proto offer','document.querySelectorAll(".prototype-offer").length===1 && document.querySelector(".prototype-offer bdi").textContent==='+json.dumps(pointer['version']))
  check(label+' exact Hebrew disclosure title', 'document.querySelector(".download-all strong").textContent === "כל הגרסאות"')
  check(label+' closed initially','!document.querySelector(".download-all").open')
  shot('01-'+label+'-closed.png',width,height)
  # Native disclosure via keyboard rather than programmatic open attribute.
  js('document.querySelector(".download-all summary").focus()')
  wait(send('Input.dispatchKeyEvent',{'type':'keyDown','key':'Enter','code':'Enter','windowsVirtualKeyCode':13,'text':'\r'}));wait(send('Input.dispatchKeyEvent',{'type':'keyUp','key':'Enter','code':'Enter','windowsVirtualKeyCode':13,'text':'\r'}));settle()
  check(label+' keyboard opens separate disclosure','document.querySelector(".download-all").open')
  shot('02-'+label+'-open.png',width,height)
  check(label+' opening never navigates','location.pathname.endsWith("index.html")')
  for newstate in ['none','invalid','failure']:
   state=newstate;js('window.__old=document.querySelector(".prototype-offer button");StartAppDownload.init()');settle()
   check(label+' '+newstate+' clears all handlers','document.querySelector(".download-direct").disabled && document.querySelector(".download-direct").onclick===null && window.__old.onclick===null && window.__old.disabled && !document.querySelector(".prototype-offer")')
   if label=='mobile':shot('03-mobile-'+newstate+'.png',width,height)
   state='ok';js('StartAppDownload.init()');settle()
  # Existing client-side router must reinitialize both directions.
  js('document.querySelectorAll("header > nav a")[0].click()');settle();settle()
  check(label+' routed guide enabled','location.pathname.endsWith("docs.html") && !document.querySelector(".doc-download button").disabled')
  shot('04-'+label+'-guide.png',width,height,True)
  js('document.querySelectorAll("header > nav a")[1].click()');settle();settle()
  check(label+' routed home reapplies','!!document.querySelector(".download-direct") && !document.querySelector(".download-direct").disabled && document.querySelectorAll(".prototype-offer").length===1')
 # Actual browser navigation click is intercepted before any binary network.
 state='ok';nav();js('document.querySelector(".prototype-offer button").click()');settle()
 check('offer click exact validated URL', 'location.href==='+json.dumps(pointer['url']))
 nav();js('document.querySelector(".download-direct").click()');settle()
 check('primary click exact validated URL','location.href==='+json.dumps(pointer['url']))
 nav('docs.html');js('document.querySelector(".doc-download button").click()');settle()
 check('guide click exact validated URL','location.href==='+json.dumps(pointer['url']))
 # The exact documented package keeps its identity-gated claims; the design
 # fallback stays hidden only for it.
 nav('docs.html');settle()
 check('documented package keeps gated claims','[...document.querySelectorAll("[data-layout-legacy-qualification]")].every(n=>!n.hidden) && document.querySelector("[data-layout-legacy-unqualified]").hidden')
 # Review-flagged unreproduced risk made deterministic: a render error fails closed.
 nav();js('window.__realRender=StartAppPointer.render;StartAppPointer.render=()=>{throw new Error("injected render failure")};StartAppDownload.init()');settle()
 check('render error fails closed','document.querySelector(".download-direct").disabled && document.querySelector(".download-direct").onclick===null && document.getElementById("hero-download-state").textContent.includes("לא ניתן לאמת") && document.querySelector("[data-prototype-offers] p").textContent==="הורדה לא זמינה"')
 js('StartAppPointer.render=window.__realRender;StartAppDownload.init()');settle()
 check('recovers after render restored','!document.querySelector(".download-direct").disabled')
 # Locale pages use same source/root with no duplicated release URL.
 expected_label={'en':'Free download','ru':'Скачать бесплатно','zh-Hans':'免费下载','ja':'無料ダウンロード','fr':'Téléchargement gratuit','es':'Descarga gratuita','de':'Kostenlos herunterladen'}
 for locale in ['en','ru','zh-Hans','ja','fr','es','de']:
  nav(locale+'/index.html');check(locale+' gated component','!document.querySelector(".download-direct").disabled && document.querySelectorAll(".prototype-offer").length===1 && document.documentElement.scrollWidth<=innerWidth')
  expected_title={'en':'All versions','ru':'Все версии','zh-Hans':'所有版本','ja':'すべてのバージョン','fr':'Toutes les versions','es':'Todas las versiones','de':'Alle Versionen'}[locale]
  check(locale+' exact disclosure title','document.querySelector(".download-all strong").textContent === '+json.dumps(expected_title))
  check(locale+' localized free-download label with actual version','document.querySelector("[data-cta-label]").textContent==='+json.dumps(expected_label[locale])+' && document.querySelector("[data-cta-version]").textContent==='+json.dumps(pointer['version']))
  check(locale+' regular heading removed','document.querySelectorAll(".download-inventory h4").length===1')
  check(locale+' offer aria-label carries localized free wording','document.querySelector(".prototype-offer button").getAttribute("aria-label").startsWith('+json.dumps(expected_label[locale])+'+" ")')
  js('document.querySelector(".download-all").open=true');shot('06-'+locale+'-open.png',390,1800)
 print('PASS',len(checks),'browser checks; exceptions',len(errors))
finally:
 (OUT/'checks.json').write_text(json.dumps({'checks':checks,'exceptions':errors,'request_count':len(requests),'source_pointer':pointer},ensure_ascii=False,indent=2));p.terminate();p.wait(timeout=10);log.close();shutil.rmtree(profile,ignore_errors=True)
