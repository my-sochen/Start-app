// Local observation-only helper. No protocol commands, target/page reads or writes.
import fs from 'node:fs';
const [endpointPath, resultPath] = process.argv.slice(2);
let ws, timer, done = false, opened = false;
const events = [];
function event(kind, fields = {}) {
  const record = {event: kind, utc: new Date().toISOString(), ...fields};
  events.push(record);
  console.log('SOCKET_EVENT ' + JSON.stringify(record));
}
function finish(state, code, fields = {}) {
  if (done) return;
  done = true;
  clearTimeout(timer);
  event('finished', {state});
  const result = {state, opened, events, ...fields};
  try { fs.writeFileSync(resultPath, JSON.stringify(result, null, 2)); }
  catch { console.error('Result write failed'); code = 6; }
  if (ws?.readyState === WebSocket.OPEN) { try { ws.close(); } catch {} }
  process.exitCode = code;
  setTimeout(() => process.exit(code), 50);
}
try {
  const lines = fs.readFileSync(endpointPath, 'utf8').trim().split(/\r?\n/);
  const port = Number(lines[0]);
  if (!Number.isInteger(port) || port < 1 || port > 65535 || !/^\/devtools\/browser\/[a-zA-Z0-9-]+$/.test(lines[1] || '')) throw Error('Invalid endpoint');
  event('connect-requested');
  ws = new WebSocket(`ws://127.0.0.1:${port}${lines[1]}`);
  timer = setTimeout(() => { event('timeout', {opened}); finish('timeout', 2); }, 35000);
  ws.addEventListener('open', () => { if (!done) { opened = true; event('open'); } });
  ws.addEventListener('message', () => { if (!done) eventOnceMessage(); });
  let messageReported = false;
  function eventOnceMessage() { if (!messageReported) { messageReported = true; event('unsolicited-message-discarded'); } }
  ws.addEventListener('error', () => { if (!done) { event('error', {opened}); finish('connection_error', 4); } });
  ws.addEventListener('close', e => { if (!done) { event('close', {closeCode:e.code, clean:e.wasClean, opened}); finish('closed', 7, {closeCode:e.code,clean:e.wasClean}); } });
} catch { event('invalid-endpoint'); finish('invalid_endpoint', 8); }
