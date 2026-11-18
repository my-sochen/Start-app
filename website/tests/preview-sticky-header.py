# Private evidence checks for the owner-requested pinned top navigation.
# Same interception pattern as the component harness (no network, no binary
# fetched); asserts the header stays pinned with correct clearance, the mobile
# docs jump bar docks below it, and navigation keeps working with real mouse
# events. Standalone: not part of the closed QA harnesses.
from pathlib import Path
import json,subprocess,tempfile,time,urllib.request,websocket,base64,mimetypes,shutil
import argparse
ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);args=ap.parse_args()
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(args.output);OUT.mkdir(parents=True,exist_ok=True)
profile=tempfile.mkdtemp(prefix='sticky-preview-');log=open(OUT/'chrome.log','w');p=subprocess.Popen(['google-chrome','--headless=new','--no-sandbox','--disable-gpu','--disable-background-networking','--disable-component-update','--host-resolver-rules=MAP * 127.0.0.1','--remote-debugging-port=9343','--remote-allow-origins=*','--user-data-dir='+profile,'about:blank'],stdout=log,stderr=log)
checks=[];errors=[];static_mode=False
try:
 for _ in range(80):
  try:tabs=json.load(urllib.request.urlopen('http://127.0.0.1:9343/json'));break
  except Exception:time.sleep(.1)
 ws=websocket.create_connection(tabs[0]['webSocketDebuggerUrl']);count=0;results={}
 def send(m,a={}):
  global count
  count+=1;ws.send(json.dumps({'id':count,'method':m,'params':a}));return count
 def wait(n):
  while n not in results:
   e=json.loads(ws.recv())
   if e.get('method')=='Fetch.requestPaused':
    d=e['params'];url=d['request']['url'];rel=url.split('/Start-app/',1)[-1].split('?',1)[0].split('#',1)[0]
    if 'releases/download/' in url:body=b'intercepted';mime='text/plain'
    else:
     f=ROOT/rel;body=f.read_bytes()if f.exists()and f.is_file()else b'';mime=mimetypes.guess_type(rel)[0]or'application/octet-stream'
    if static_mode and mime=='text/html':
     import re
     body=re.sub(rb'<script\b[^>]*>.*?</script>',b'',body,flags=re.S|re.I)
    send('Fetch.fulfillRequest',{'requestId':d['requestId'],'responseCode':200,'responseHeaders':[{'name':'Content-Type','value':mime}],'body':base64.b64encode(body).decode()})
   elif e.get('method')=='Runtime.exceptionThrown':errors.append(e['params'])
   elif'id'in e:results[e['id']]=e
  r=results.pop(n)
  if 'error'in r:raise RuntimeError(r)
  return r.get('result',{})
 def js(s):
  r=wait(send('Runtime.evaluate',{'expression':s,'returnByValue':True,'awaitPromise':True}))
  if 'exceptionDetails'in r:raise RuntimeError(r)
  return r.get('result',{}).get('value')
 def check(name,expr):
  value=js(expr);checks.append({'name':name,'pass':bool(value)})
  if not value:raise AssertionError(name)
 def settle():
  try:js('new Promise(r=>setTimeout(r,110))')
  except RuntimeError as e:
   if 'navigated or closed' not in str(e):raise
   js('new Promise(r=>setTimeout(r,110))')
 def nav(path,width,height):
  wait(send('Emulation.setDeviceMetricsOverride',{'width':width,'height':height,'deviceScaleFactor':1,'mobile':False}));wait(send('Page.navigate',{'url':'https://my-sochen.github.io/Start-app/'+path}));js('document.fonts.ready.then(()=>true)');settle()
 def click(sel,tag,expect):
  box=js('(e=>{const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})(document.querySelector('+json.dumps(sel)+'))')
  for t in ['mousePressed','mouseReleased']:
   wait(send('Input.dispatchMouseEvent',{'type':t,'x':box['x'],'y':box['y'],'button':'left','clickCount':1}))
  settle();settle()
  checks.append({'name':tag,'pass':js('location.pathname.endsWith('+json.dumps(expect)+')')})
  if not js('location.pathname.endsWith('+json.dumps(expect)+')'):raise AssertionError(tag)
 wait(send('Page.enable'));wait(send('Page.bringToFront'));wait(send('Runtime.enable'));wait(send('Fetch.enable',{'patterns':[{'urlPattern':'*'}]}))
 # Desktop home: pinned through the whole scroll, opaque, above content.
 nav('index.html',1100,700)
 check('desktop header sticky','getComputedStyle(document.querySelector("header")).position==="sticky"')
 js('window.scrollTo(0,document.documentElement.scrollHeight)');settle()
 check('desktop header pinned at top through scroll','Math.round(document.querySelector("header").getBoundingClientRect().top)===0')
 check('desktop header opaque','getComputedStyle(document.querySelector("header")).backgroundColor==="rgb(243, 246, 252)"')
 check('desktop header above hero content','getComputedStyle(document.querySelector("header")).zIndex==="25"')
 js('window.scrollTo(0,0)');settle()
 check('desktop initial layout unchanged','Math.round(document.querySelector("main").offsetTop)===85')
 # Real nav click mid-scroll: guide link routes to the guide.
 js('window.scrollTo(0,120)');settle()
 click('header > nav a[href="docs.html"]','desktop mid-scroll real nav click reaches guide','docs.html')
 # Disable page scripts: static wrapper behavior is the hosting contract.
 static_mode=True
 # Shared wrapper follows actual header height, including narrow containers.
 for width,inner,locale in [(390,None,''),(272,None,''),(390,257,''),(700,257,''),(320,None,'en/'),(390,257,'de/')]:
  nav(locale+'docs.html',width,700)
  if inner:js('document.body.style.width="'+str(inner)+'px";document.body.style.margin="0 auto"')
  js('window.scrollTo({top:600,behavior:"instant"})');settle()
  tag=str(width)+'/'+str(inner)+' '+locale
  check(tag+' shared wrapper pinned','Math.abs(document.querySelector(".pinned-top").getBoundingClientRect().top)<1')
  check(tag+' jump follows actual header bottom','Math.abs(document.querySelector(".docs-jump").getBoundingClientRect().top-document.querySelector("header").getBoundingClientRect().bottom)<1')
  check(tag+' jump real hit target','(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2))})(document.querySelector(".docs-jump a"))')
  check(tag+' header nav real hit target','(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2))})(document.querySelector("header > nav a"))')
  if width==390 and inner==257 and locale=='':
   raw=base64.b64decode(wait(send('Page.captureScreenshot',{'format':'png'}))['data']);(OUT/'narrow-container-docs.png').write_bytes(raw)
 static_mode=False
 # Actual router transitions must preserve and remove the shared wrapper.
 nav('index.html',272,700)
 click('header > nav a[href="docs.html"]','narrow Home to Guide nav works','docs.html')
 check('router creates wrapper and jump','document.querySelectorAll(".pinned-top").length===1 && document.querySelectorAll(".docs-jump").length===1')
 js('window.scrollTo({top:600,behavior:"instant"})');settle()
 raw=base64.b64decode(wait(send('Page.captureScreenshot',{'format':'png'}))['data']);(OUT/'narrow-viewport-docs.png').write_bytes(raw)
 click('header > nav a[href="index.html#download"]','narrow Guide to Home nav works','index.html')
 check('router removes guide wrapper','!document.querySelector(".pinned-top") && !document.querySelector(".docs-jump") && getComputedStyle(document.querySelector("header")).position==="sticky"')
 raw=base64.b64decode(wait(send('Page.captureScreenshot',{'format':'png'}))['data']);(OUT/'narrow-home-pinned.png').write_bytes(raw)
 print('PASS',len(checks),'sticky header checks; exceptions',len(errors))
finally:
 (OUT/'checks.json').write_text(json.dumps({'checks':checks,'exceptions':errors},ensure_ascii=False,indent=2));p.terminate();p.wait(timeout=10);log.close();shutil.rmtree(profile,ignore_errors=True)
