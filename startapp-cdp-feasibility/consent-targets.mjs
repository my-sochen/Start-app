// LOCAL candidate only. One socket, one Target.getTargets after explicit local UI invocation signal.
// No attaching, evaluating, navigation, target activation, cookies or arbitrary payload logging.
import fs from 'node:fs';
import {pathToFileURL} from 'node:url';
export function validManifest(v) {
 return Array.isArray(v)&&v.length===3&&v.every((x,i)=>x&&x.label==='ABC'[i]&&x.title==='StartAppFixture'+x.label&&typeof x.url==='string'&&/^file:\/\/\//.test(x.url)&&x.url.endsWith('/fixture-'+x.label+'.html'))&&new Set(v.map(x=>x.url)).size===3&&new Set(v.map(x=>x.url.slice(0,x.url.lastIndexOf('/')))).size===1;
}
export function fixtureTargets(targets,manifest) {
 if(!validManifest(manifest)||!Array.isArray(targets)||targets.length>32)throw Error('Invalid bounded targets');
 const out=[];
 for(const m of manifest){
  const hits=targets.filter(t=>t&&t.type==='page'&&t.url===m.url);
  if(hits.length!==1||hits[0].title!==m.title||typeof hits[0].targetId!=='string'||!/^[a-zA-Z0-9-]{1,128}$/.test(hits[0].targetId))throw Error('Fixture metadata mismatch');
  out.push({label:m.label,title:m.title,url:m.url,targetId:hits[0].targetId,type:'page'});
 }
 if(new Set(out.map(t=>t.targetId)).size!==3)throw Error('Duplicate fixture target IDs');
 return out;
}
export async function run(argv) {
 const [endpointPath,resultPath,signalPath,manifestPath]=argv;
 let ws,timer,signalTimer,done=false,opened=false,sent=false,signaled=false,manifest;
 const events=[];
 const event=(kind)=>{const e={event:kind,utc:new Date().toISOString()};events.push(e);console.log('CONSENT_EVENT '+JSON.stringify(e));};
 function finish(state,code,targets=[]) {
  if(done)return;done=true;clearTimeout(timer);clearInterval(signalTimer);event('finished');
  try{fs.writeFileSync(resultPath,JSON.stringify({state,opened,signaled,sent,events,targets}));}catch{code=6;console.error('Result write failed');}
  if(ws?.readyState===WebSocket.OPEN)try{ws.close();}catch{}
  process.exitCode=code;setTimeout(()=>process.exit(code),50);
 }
 function maybeSend(){
  if(done||sent||!opened)return;
  if(!fs.existsSync(signalPath))return;
  let s;try{s=JSON.parse(fs.readFileSync(signalPath,'utf8').replace(/^\uFEFF/,''));}catch{return finish('invalid_signal',9);}
  if(!s||s.invoked!==true||s.kind!=='native-owned-single-Allow')return finish('invalid_signal',9);
  signaled=true;event('invoke-signal');sent=true;event('request-sent');
  try{ws.send(JSON.stringify({id:1,method:'Target.getTargets'}));}catch{finish('send_error',4);}
 }
 try{
  manifest=JSON.parse(fs.readFileSync(manifestPath,'utf8').replace(/^\uFEFF/,''));if(!validManifest(manifest))throw Error();
  if(fs.existsSync(signalPath))throw Error(); // stale signal cannot authorize a connection
  const lines=fs.readFileSync(endpointPath,'utf8').trim().split(/\r?\n/),port=Number(lines[0]);
  if(lines.length!==2||!Number.isInteger(port)||port<1||port>65535||!/^\/devtools\/browser\/[a-zA-Z0-9-]+$/.test(lines[1]))throw Error();
  event('connect-requested');ws=new WebSocket(`ws://127.0.0.1:${port}${lines[1]}`);
  timer=setTimeout(()=>{event('timeout');finish('timeout',2);},45000);
  signalTimer=setInterval(maybeSend,100);
  ws.addEventListener('open',()=>{if(done)return;opened=true;event('open');maybeSend();});
  ws.addEventListener('message',e=>{
   if(done)return;
   if(!sent)return finish('unsolicited_message',10);
   if(typeof e.data!=='string'||Buffer.byteLength(e.data)>262144)return finish('invalid_response',11);
   let r;try{r=JSON.parse(e.data);}catch{return finish('invalid_response',11);}
   if(r?.id!==1||r.error||!r.result||!Array.isArray(r.result.targetInfos))return finish('invalid_response',11);
   try{const out=fixtureTargets(r.result.targetInfos,manifest);event('response-matched');finish('metadata_collected',0,out);}catch{finish('fixture_mismatch',12);}
  });
  ws.addEventListener('error',()=>{if(!done){event('error');finish('connection_error',4);}});
  ws.addEventListener('close',()=>{if(!done){event('close');finish('closed',7);}});
 }catch{event('invalid-input');finish('invalid_input',8);}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)await run(process.argv.slice(2));
