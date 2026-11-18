# Private motion-preview evidence for the owner-tentative content/background
# depth demo. Serves the local working tree through the same interception
# pattern as the component harness (no network, no binary fetched), asserts the
# background art lags the scroll while content stays in one layer, verifies
# real hit targets with genuine mouse events at several scroll positions, and
# captures GIF frames. Not part of the closed QA harnesses.
from pathlib import Path
import json,subprocess,tempfile,time,urllib.request,websocket,base64,mimetypes,shutil
import argparse
ap=argparse.ArgumentParser();ap.add_argument('--pointer-snapshot',required=True);ap.add_argument('--output',required=True);args=ap.parse_args()
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(args.output);OUT.mkdir(parents=True,exist_ok=True)
pointer=json.loads(Path(args.pointer_snapshot).read_text())
profile=tempfile.mkdtemp(prefix='motion-preview-');log=open(OUT/'chrome.log','w');p=subprocess.Popen(['google-chrome','--headless=new','--no-sandbox','--disable-gpu','--disable-background-networking','--disable-component-update','--host-resolver-rules=MAP * 127.0.0.1','--remote-debugging-port=9340','--remote-allow-origins=*','--user-data-dir='+profile,'about:blank'],stdout=log,stderr=log)
checks=[];errors=[]
try:
 for _ in range(80):
  try:tabs=json.load(urllib.request.urlopen('http://127.0.0.1:9340/json'));break
  except Exception:time.sleep(.1)
 ws=websocket.create_connection(tabs[0]['webSocketDebuggerUrl']);count=0;results={}
 def send(m,a={}):
  global count
  count+=1;ws.send(json.dumps({'id':count,'method':m,'params':a}));return count
 def wait(n):
  while n not in results:
   e=json.loads(ws.recv())
   if e.get('method')=='Fetch.requestPaused':
    d=e['params'];url=d['request']['url'];rel=url.split('/Start-app/',1)[-1].split('?',1)[0].split('#',1)[0];status=200
    if rel=='pointer.json':
     body=json.dumps(pointer).encode();mime='application/json'
    elif 'releases/download/' in url:
     body=b'Navigation intercepted by private preview, no binary fetched.';mime='text/plain'
    else:
     f=ROOT/rel
     body=f.read_bytes()if f.exists()and f.is_file()else b'';mime=mimetypes.guess_type(rel)[0]or'application/octet-stream'
    send('Fetch.fulfillRequest',{'requestId':d['requestId'],'responseCode':status,'responseHeaders':[{'name':'Content-Type','value':mime}],'body':base64.b64encode(body).decode()})
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
 def frame(name):
  shot=wait(send('Page.captureScreenshot',{'format':'png'}));raw=base64.b64decode(shot['data'])
  from PIL import Image
  import io
  im=Image.open(io.BytesIO(raw));im.save(OUT/name)
  return im
 def click(sel,tag):
  box=js('(e=>{const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})(document.querySelector('+json.dumps(sel)+'))')
  for t in ['mousePressed','mouseReleased']:
   wait(send('Input.dispatchMouseEvent',{'type':t,'x':box['x'],'y':box['y'],'button':'left','clickCount':1}))
  settle()
  checks.append({'name':tag+' real mouse click lands on exact validated URL','pass':js('location.href')==pointer['url']})
  if js('location.href')!=pointer['url']:raise AssertionError(tag+' real click missed')
  nav('index.html',1100,700)
 wait(send('Page.enable'));wait(send('Page.bringToFront'));wait(send('Runtime.enable'));wait(send('Fetch.enable',{'patterns':[{'urlPattern':'*'}]}))
 def scroll_span():
  return int(js('document.documentElement.scrollHeight-innerHeight'))
 def depth_pass(tag,path,width,height,art_sel,anim):
  nav(path,width,height)
  span=scroll_span()
  assert span>60,'page must scroll for the preview'
  check(tag+' background art drift applied','getComputedStyle(document.querySelector("'+art_sel+'")).animationName==='+"'"+anim+"'")
  check(tag+' art clipped to its box','document.querySelector("'+art_sel+'").closest(".page-line-art,.page-line-top,.page-line-bottom")!==null')
  arts=[];tops=[];frames=[]
  steps=12
  for i in range(steps+1):
   y=round(span*i/steps);js('window.scrollTo(0,'+str(y)+')');settle()
   arts.append(js('getComputedStyle(document.querySelector("'+art_sel+'")).transform'))
   tops.append(js('document.querySelector(".download-card").offsetTop'))
   if tag=='desktop-he':frames.append(frame(tag+'-f%02d.png'%i))
  checks.append({'name':tag+' art moves relative to scrolling content','pass':arts[0]!=arts[-1]})
  if arts[0]==arts[-1]:raise AssertionError(tag+' art static')
  checks.append({'name':tag+' content stays one layer (no layout shift)','pass':len(set(tops))==1})
  if len(set(tops))!=1:raise AssertionError(tag+' layout shifted: '+json.dumps(tops))
  check(tag+' cards carry no independent transform','getComputedStyle(document.querySelector(".download-card")).transform==="none"')
  if tag=='desktop-he':frames[0].save(OUT/'depth-full-scene.gif',save_all=True,append_images=frames[1:],duration=160,loop=0)
  return span
 # Desktop Hebrew (RTL) home: full-scene evidence frames captured here.
 span=depth_pass('desktop-he','index.html',1100,700,'.page-line-art img','background-drift')
 # Real hit targets through scroll: genuine mouse events at the visual center
 # of the primary button at top, middle and bottom of the scroll range.
 for y,tag in [(0,'top'),(span//2,'mid'),(span,'bottom')]:
  js('window.scrollTo(0,'+str(y)+')');settle();click('.download-direct','desktop-he '+tag)
 # Guide page finding: docs.html has no decorative background layer, so no
 # depth motion exists there by design; the guide card stays fully static.
 nav('docs.html',1100,700)
 check('guide has no background art layer','document.querySelector(".page-line-art,.page-line-top,.page-line-bottom")===null')
 check('guide card fully static','getComputedStyle(document.querySelector(".doc-download")).transform==="none" && getComputedStyle(document.querySelector(".doc-download")).animationName==="none"')
 # Reduced motion: drift fully disabled at every scroll position.
 wait(send('Emulation.setEmulatedMedia',{'features':[{'name':'prefers-reduced-motion','value':'reduce'}]}))
 nav('index.html',1100,700)
 check('reduced motion removes drift','getComputedStyle(document.querySelector(".page-line-art img")).animationName==="none"')
 still=[]
 for y in [0,scroll_span()]:
  js('window.scrollTo(0,'+str(y)+')');settle()
  still.append(js('getComputedStyle(document.querySelector(".page-line-art img")).transform'))
 check('reduced motion no drift at any scroll',json.dumps(still)+'.every(v=>v==="none")')
 frames=[frame('reduced-f0.png'),frame('reduced-f1.png')]
 frames[0].save(OUT/'reduced-motion-static.gif',save_all=True,append_images=frames[1:],duration=400,loop=0)
 wait(send('Emulation.setEmulatedMedia',{'features':[{'name':'prefers-reduced-motion','value':''}]}))
 # Mobile (top/bottom art) and LTR English passes, assertions only.
 nav('index.html',390,700)
 check('mobile art drift applied','getComputedStyle(document.querySelector(".page-line-top img")).animationName==="background-drift-mobile"')
 js('window.scrollTo(0,0)');settle();m0=js('getComputedStyle(document.querySelector(".page-line-top img")).transform')
 js('window.scrollTo(0,document.documentElement.scrollHeight)');settle();m1=js('getComputedStyle(document.querySelector(".page-line-top img")).transform')
 checks.append({'name':'mobile art moves relative to content','pass':m0!=m1})
 if m0==m1:raise AssertionError('mobile art static')
 js('window.scrollTo(0,Math.floor((document.documentElement.scrollHeight-innerHeight)/2))');settle();click('.download-direct','mobile-he mid')
 nav('en/index.html',1100,700)
 check('english LTR art drift applied','getComputedStyle(document.querySelector(".page-line-art img")).animationName==="background-drift"')
 js('window.scrollTo(0,0)');settle();e0=js('getComputedStyle(document.querySelector(".page-line-art img")).transform')
 js('window.scrollTo(0,document.documentElement.scrollHeight)');settle();e1=js('getComputedStyle(document.querySelector(".page-line-art img")).transform')
 checks.append({'name':'english LTR art moves relative to content','pass':e0!=e1})
 if e0==e1:raise AssertionError('english art static')
 print('PASS',len(checks),'depth preview checks; exceptions',len(errors))
finally:
 (OUT/'checks.json').write_text(json.dumps({'checks':checks,'exceptions':errors},ensure_ascii=False,indent=2));p.terminate();p.wait(timeout=10);log.close();shutil.rmtree(profile,ignore_errors=True)
