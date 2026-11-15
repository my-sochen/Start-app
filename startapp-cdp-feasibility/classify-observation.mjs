// Observation-only evidence audit. A green job NEVER means browser/consent/metadata success.
// Usage: node classify-observation.mjs <outdir> <childExit>
import {readFileSync,existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {join} from 'node:path';
const [dir,exitArg]=process.argv.slice(2);
const reasons=[]; const note=x=>reasons.push(x);
const required=['version','navigation','inspect-before','fixture','before-window1','before-window2','inspect-toggle','endpoint','socket-observation','prompt-observation','first-consent','enumeration','fixture-selection-unchanged','after-window1','after-window2','reconnect-denied','disable','post-disable-unavailable','cleanup','complete'];
const baseline=['inspect-before','before-window1','before-window2','after-window1','after-window2'];
const prompt=['prompt-onset','prompt-end'];
const optional=['version-floor','probe',...prompt];
const forbiddenPass=['first-consent','enumeration','reconnect-denied','post-disable-unavailable'];
const browsers=['chrome','edge'];
const obj=x=>x!==null&&typeof x==='object'&&!Array.isArray(x);
const arr=x=>Array.isArray(x)?x:x==null?[]:[x];
const integer=x=>Number.isSafeInteger(x)&&x>0;
let records=[];
try{const p=JSON.parse(readFileSync(join(dir,'assertions.json'),'utf8').replace(/^\uFEFF/,''));records=Array.isArray(p)?p:[p];}catch{note('assertions missing/unparseable');}
const pick=(b,s)=>records.filter(r=>obj(r)&&r.browser===b&&r.step===s);
const status=(b,s)=>pick(b,s).length===1?pick(b,s)[0].status:'MISSING';
const detail=r=>{try{const d=JSON.parse(r.detail);return obj(d)?d:null;}catch{return null;}};
let anyFail=false,images=0,bytes=0;
for(const r of records){
 if(obj(r)&&r.status==='FAIL')anyFail=true;
 if(!obj(r)){note('nonobject record');continue;}
 if(!['all',...browsers].includes(r.browser)){note('unknown browser');continue;}
 if(!['PASS','FAIL','INFO','UNKNOWN','UNAVAILABLE'].includes(r.status))note('invalid status');
 if(typeof r.detail!=='string'||typeof r.utc!=='string'||!Number.isFinite(Date.parse(r.utc)))note('invalid record detail/time');
 const known=r.browser==='all'?['scope','complete','preflight']:[...required,...optional];
 if(!known.includes(r.step))note('unknown step '+r.step);
}
for(const s of ['scope','complete'])if(pick('all',s).length!==1||status('all',s)!=='INFO')note('all/'+s+' must be exactly oneINFO');
if(pick('all','preflight').length)note('preflight incomplete');
function validSelection(v){
 const a=arr(v);if(a.length!==3)return false;
 if(!a.every(x=>obj(x)&&integer(x.hwnd)&&typeof x.title==='string'&&typeof x.selected==='boolean'))return false;
 const h=new Set(a.map(x=>x.hwnd)),t=new Set(a.map(x=>x.title.split(/\s/)[0]));
 return h.size===2&&t.size===3&&[...t].every(x=>/^StartAppFixture[ABC]$/.test(x))&&[...h].every(h=>a.filter(x=>x.hwnd===h&&x.selected).length===1);
}
const canon=x=>JSON.stringify(arr(x).map(x=>[x.hwnd,x.title,x.selected]).sort());
function validIdentity(s){
 if(!obj(s)||s.eligible!==true||!['inspectUnique','inspectFresh','foregroundStable','visible','rectAgrees','rectWithinParent','parentWithinScreen','sizeAllowed'].every(k=>s[k]===true)||s.minimized!==false||s.offscreen!==false)return false;
 const chain=arr(s.chain),pids=arr(s.profilePids);
 if(!integer(s.inspect)||!integer(s.foreground)||s.inspect===s.foreground||s.rootOwner!==s.inspect||chain.length<2||chain.length>8||!pids.length||!pids.every(integer))return false;
 if(!chain.every(x=>obj(x)&&integer(x.hwnd)&&integer(x.pid)&&pids.includes(x.pid)&&Number.isSafeInteger(x.owner)&&x.owner>=0))return false;
 if(new Set(chain.map(x=>x.hwnd)).size!==chain.length||chain[0].hwnd!==s.foreground||chain.at(-1).hwnd!==s.inspect||chain.some((x,i)=>i<chain.length-1&&x.owner!==chain[i+1].hwnd))return false;
 if(s.uiaPid!==chain[0].pid||s.inspectUiaPid!==chain.at(-1).pid||s.uiaHwnd!==s.foreground)return false;
 if(![s.runtimeId,s.parentRuntimeId].every(v=>Array.isArray(v)&&v.length>0&&v.every(Number.isSafeInteger)))return false;
 const r=s.parentRect,q=s.popupRect;
 if(![r,q].every(x=>Array.isArray(x)&&x.length===4&&x.every(Number.isFinite)))return false;
 if(r[2]-r[0]<300||r[2]-r[0]>1600||r[3]-r[1]<200||r[3]-r[1]>1100)return false;
 return q[0]>=r[0]&&q[1]>=r[1]&&q[2]<=r[2]&&q[3]<=r[3]&&q[2]>q[0]&&q[3]>q[1];
}
function validSocket(d){
 if(!obj(d)||d.promptSeen!==false||d.decisionApplied!==false||d.decision!=='observe-only'||typeof d.onsetEvidence!=='boolean'||typeof d.endEvidence!=='boolean'||!Number.isInteger(d.helperExit)||!obj(d.result))return false;
 const r=d.result;if(r.state==='no_result'||Object.keys(r).some(k=>!['state','opened','events','closeCode','clean'].includes(k)))return false;
 const codes={timeout:2,connection_error:4,closed:7,invalid_endpoint:8};
 if(codes[r.state]!==d.helperExit||typeof r.opened!=='boolean'||!Array.isArray(r.events)||r.events.length<2||r.events.length>8||'targets'in r)return false;
 const allowed={event:1,utc:1,state:1,opened:1,closeCode:1,clean:1};
 const names=['connect-requested','open','timeout','error','close','finished','invalid-endpoint','unsolicited-message-discarded'];
 if(!r.events.every(e=>obj(e)&&Object.keys(e).every(k=>allowed[k])&&names.includes(e.event)&&typeof e.utc==='string'&&Number.isFinite(Date.parse(e.utc))))return false;
 if(r.events.some((e,i)=>i>0&&Date.parse(e.utc)<Date.parse(r.events[i-1].utc)))return false;
 for(const e of r.events){if('opened'in e&&typeof e.opened!=='boolean'||'closeCode'in e&&!Number.isInteger(e.closeCode)||'clean'in e&&typeof e.clean!=='boolean')return false;}
 const seq=r.events.map(e=>e.event),last=r.events.at(-1);
 if(new Set(seq).size!==seq.length||last.event!=='finished'||last.state!==r.state)return false;
 if((seq.includes('open'))!==r.opened||seq.includes('unsolicited-message-discarded')&&!r.opened)return false;
 const terminal={timeout:'timeout',connection_error:'error',closed:'close',invalid_endpoint:'invalid-endpoint'}[r.state];
 if(seq.at(-2)!==terminal)return false;
 const expected=['connect-requested',...(r.opened?['open']:[]),...(seq.includes('unsolicited-message-discarded')?['unsolicited-message-discarded']:[]),terminal,'finished'];
 if(r.state!=='invalid_endpoint'&&JSON.stringify(seq)!==JSON.stringify(expected))return false;
 if(r.state==='invalid_endpoint')return seq.length===2&&!r.opened;
 if(seq[0]!=='connect-requested')return false;
 if(r.opened&&seq.indexOf('open')!==1)return false;
 if(r.state==='closed'&&(!Number.isInteger(r.closeCode)||typeof r.clean!=='boolean'||r.events.at(-2).closeCode!==r.closeCode||r.events.at(-2).clean!==r.clean))return false;
 if(['timeout','connection_error','closed'].includes(r.state)&&r.events.at(-2).opened!==r.opened)return false;
 return true;
}
let collected=true;
for(const b of browsers){
 for(const s of required){
  const a=pick(b,s);if(a.length!==1){note(`${b}/${s} count${a.length}`);continue;}
  const x=a[0].status;
  if(forbiddenPass.includes(s)){if(x!=='UNKNOWN')note(`${b}/${s} must remainUNKNOWN`);}
  else if(['version','complete','socket-observation','prompt-observation'].includes(s)){
   if(!['INFO','UNKNOWN','UNAVAILABLE'].includes(x)||s==='complete'&&x!=='INFO')note(`${b}/${s} invalid observationstatus`);
  }else if(!['PASS','UNKNOWN','UNAVAILABLE','FAIL'].includes(x))note(`${b}/${s} invalid phasestatus`);
 }
 for(const s of optional){const a=pick(b,s);if(a.length>1)note(`${b}/${s} duplicate`);if(a.length&&!(prompt.includes(s)?a[0].status==='INFO':['UNKNOWN','UNAVAILABLE','FAIL'].includes(a[0].status)))note(`${b}/${s} optionalinvalid`);}
 if(status(b,'version')!=='INFO'){collected=false;note(`${b} versionnotINFOforfullobservation`);}
 if(pick(b,'version-floor').length||pick(b,'probe').length)collected=false;
 if(pick(b,'version-floor').length||pick(b,'probe').length)note(`${b} earlyterminationrowinfullobservation`);
 for(const s of ['navigation','fixture','inspect-toggle','endpoint','fixture-selection-unchanged','disable','cleanup',...baseline])if(status(b,s)!=='PASS')collected=false;
 const nav=detail(pick(b,'navigation')[0]||{}),uri=b==='chrome'?'chrome://inspect/#remote-debugging':'edge://inspect/#remote-debugging';
 if(status(b,'navigation')==='PASS'&&(!nav||nav.confirmed!==true||nav.omnibox!==uri||nav.intended!==uri))note(`${b} navigationdetail`);
 const selection=pick(b,'fixture-selection-unchanged')[0],sd=detail(selection||{});
 if(selection&&['PASS','FAIL'].includes(selection.status)){
  const valid=sd&&validSelection(sd.before)&&validSelection(sd.after),same=valid&&canon(sd.before)===canon(sd.after);
  if(selection.status==='PASS'&&!same||selection.status==='FAIL'&&(!valid||same))note(`${b} selectioncontradiction`);
 }
 const sock=detail(pick(b,'socket-observation')[0]||{});
 if(status(b,'version')==='INFO'&&!String(pick(b,'version')[0]?.detail||'').trim())note(`${b} versionempty`);
 const enumDetail=detail(pick(b,'enumeration')[0]||{});
 if(sock&&JSON.stringify(enumDetail)!==JSON.stringify(sock))note(`${b} enumerationmustonlyrepeatobservationresult`);
 if(status(b,'socket-observation')==='INFO'&&!validSocket(sock))note(`${b} malformedhandshakeevidence`);
 if(status(b,'socket-observation')!=='INFO'||!validSocket(sock))collected=false;
 const observed=status(b,'prompt-observation')==='INFO';
 if(!observed)collected=false;
 if(observed&&(!sock?.onsetEvidence||!sock?.endEvidence))note(`${b} summarydoesnotmatchemittedpictures`);
 let perBrowser=0;
 for(const s of [...baseline,...prompt]){
  const a=pick(b,s);if(!a.length)continue;
  const r=a[0],expected=prompt.includes(s)?'INFO':'PASS';if(r.status!==expected)continue;
  perBrowser++;images++;const d=detail(r);
  if(!d||!integer(d.bytes)||d.bytes>262144||!Number.isInteger(d.width)||!Number.isInteger(d.height)||d.width<300||d.width>1600||d.height<200||d.height>1100||!/^[a-f0-9]{64}$/.test(d.sha256||'')){note(`${b}/${s} invalidimagedetail`);continue;}
  bytes+=d.bytes;
  if(prompt.includes(s)){
   if(!validIdentity(d.identity))note(`${b}/${s} invalidnativeidentity`);
   const rect=d.identity?.parentRect;
   if(!Array.isArray(rect)||rect.length!==4||rect[2]-rect[0]!==d.width||rect[3]-rect[1]!==d.height)note(`${b}/${s} PNGdoesnotmatchnativeparentdimensions`);
  }
  const f=join(dir,`${b}-${s}.png`);
  if(!existsSync(f)){note(`${b}/${s} PNGmissing`);continue;}
  const buf=readFileSync(f);
  if(buf.length!==d.bytes||createHash('sha256').update(buf).digest('hex')!==d.sha256||buf.length<24||buf.subarray(0,8).toString('hex')!=='89504e470d0a1a0a'||buf.readUInt32BE(16)!==d.width||buf.readUInt32BE(20)!==d.height)note(`${b}/${s} PNGintegrity`);
 }
 if(perBrowser>7)note(`${b} imagecountcap`);
 for(const s of prompt){if(observed&&pick(b,s).length!==1)note(`${b}/${s} missingforobservation`);}
 if(sock){for(const [k,s]of[['onsetEvidence','prompt-onset'],['endEvidence','prompt-end']])if(sock[k]!== (pick(b,s).length===1))note(`${b}/${s} helperflagmismatch`);}
 for(const [s,deps]of[['inspect-toggle',['navigation','fixture','before-window1','before-window2']],['endpoint',['inspect-toggle']],['fixture-selection-unchanged',['fixture']]])if(status(b,s)==='PASS')for(const d of deps)if(status(b,d)!=='PASS')note(`${b}/${s} dependency${d}`);
}
if(images>14||bytes>3670016)note('globalimagecap');
if(exitArg!=='2')note('observationchildmustexit2');
const verdict=anyFail?'TEST_FAILURE':reasons.length||!collected?'INCONCLUSIVE':'OBSERVATION_COLLECTED';
console.log(`OBS_VERDICT ${verdict} childExit=${exitArg} reasons=${reasons.length} images=${images}`);
for(const r of reasons)console.log('OBS_REASON '+r);
for(const b of browsers)console.log(`OBS_PHASES ${b} `+required.map(s=>`${s}=${status(b,s)}`).join(','));
console.log('OBS_NOTE FULL_GATE NOT_PASS. Consent, metadata, reconnectdenial and causalrevocation remain UNKNOWN. OBSERVATION_COLLECTED means structurally complete local evidence only, not decoded/pixelvalid/nativeindependent proof. Actual PNGdecode/executor+main pixelreview mandatory. If bounded attribution refuses images, report/stop, no retry or guardrelaxation.');
process.exit(anyFail?1:0);
