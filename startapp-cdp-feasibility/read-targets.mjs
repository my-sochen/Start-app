// Isolated-profile diagnostic only. No activation, page reads or browser writes.
import fs from 'node:fs';
const [endpointPath, resultPath] = process.argv.slice(2);
let ws, timer, done = false;
function finish(result, code) {
  if (done) return;
  done = true;
  clearTimeout(timer);
  try { fs.writeFileSync(resultPath, JSON.stringify(result, null, 2)); console.log(JSON.stringify(result)); }
  catch (e) { console.error('Result write failed:', e.message); code = 6; }
  if (ws?.readyState === WebSocket.OPEN) { try { ws.close(); } catch {} }
  process.exitCode = code;
  setTimeout(() => process.exit(code), 50);
}
try {
  const lines = fs.readFileSync(endpointPath, 'utf8').trim().split(/\r?\n/);
  const port = Number(lines[0]);
  if (!Number.isInteger(port) || port < 1 || port > 65535 || !/^\/devtools\/browser\/[a-zA-Z0-9-]+$/.test(lines[1] || '')) throw Error('Unexpected local endpoint format');
  ws = new WebSocket(`ws://127.0.0.1:${port}${lines[1]}`);
  timer = setTimeout(() => finish({state:'timeout'}, 2), 35000);
  ws.addEventListener('open', () => { if (!done) { console.log('CONNECTED'); ws.send(JSON.stringify({id:1, method:'Target.getTargets'})); } });
  ws.addEventListener('message', e => {
    if (done) return;
    try {
      const m = JSON.parse(e.data);
      if (m.id !== 1) return;
      if (m.error) return finish({state:'protocol_error', error:m.error}, 3);
      if (!Array.isArray(m.result?.targetInfos)) throw Error('Missing targetInfos array');
      // Retain only synthetic fixture pages. Never persist other page metadata.
      const targets = m.result.targetInfos.filter(t => t.type === 'page' && /^StartAppFixture[ABC]$/.test(t.title) && /^file:\/\//.test(t.url)).map(t => ({id:t.targetId,title:t.title,url:t.url}));
      finish({state:'enumerated',targets}, 0);
    } catch (e) { finish({state:'invalid_response',message:e.message}, 5); }
  });
  ws.addEventListener('error', () => finish({state:'connection_error'}, 4));
  ws.addEventListener('close', e => finish({state:'closed',closeCode:e.code,clean:e.wasClean}, 7));
} catch (e) { finish({state:'invalid_endpoint',message:e.message}, 8); }
