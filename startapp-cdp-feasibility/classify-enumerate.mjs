// Strict completeness classifier for enumerate-tabs.ps1 output. Plain Node, no dependencies.
// Usage: node classify-enumerate.mjs <out-dir> <child-exit-code>
// Verdicts: TEST_FAILURE (exit 1), INCONCLUSIVE (exit 0), EVIDENCE_COLLECTED (exit 0).
// None of them is a feasibility, consent or access-control PASS. A green job is not browser success.
// The full matrix can never PASS: post-disable-unavailable is intentionally UNKNOWN in this scope.
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
const [dir, exitArg] = process.argv.slice(2);
const childExit = /^[0-9]+$/.test(String(exitArg ?? '')) ? Number(exitArg) : NaN; // strict nonempty digits only
const problems = [];
const note = (m) => problems.push(m);
let records = [];
try {
  let parsed = JSON.parse(readFileSync(join(dir, 'assertions.json'), 'utf8').replace(/^\uFEFF/, ''));
  if (!Array.isArray(parsed)) parsed = [parsed];
  records = parsed;
} catch (e) { note('INFRA assertions.json missing or unparseable'); }
if (!Number.isInteger(childExit)) note('INFRA child exit code missing');
const pick = (b, s) => records.filter((r) => r && r.browser === b && r.step === s);
const phaseStatuses = ['PASS', 'UNKNOWN', 'UNAVAILABLE', 'FAIL'];
const required = ['version', 'navigation', 'inspect-before', 'fixture', 'before-window1', 'before-window2', 'inspect-toggle', 'endpoint', 'first-consent', 'enumeration', 'fixture-selection-unchanged', 'after-window1', 'after-window2', 'reconnect-denied', 'disable', 'post-disable-unavailable', 'cleanup', 'complete'];
const optional = ['version-floor', 'probe', 'first-prompt', 'first-unattributed', 'reconnect-deny-prompt', 'reconnect-deny-unattributed'];
const imagePhases = ['inspect-before', 'before-window1', 'before-window2', 'after-window1', 'after-window2', 'first-prompt', 'first-unattributed', 'reconnect-deny-prompt', 'reconnect-deny-unattributed'];
const intendedUri = { chrome: 'chrome://inspect/#remote-debugging', edge: 'edge://inspect/#remote-debugging' };
const okStatus = new Set(['INFO', 'PASS', 'UNKNOWN', 'UNAVAILABLE', 'FAIL']);
const st = (b, s) => { const r = pick(b, s); return r.length === 1 ? r[0].status : undefined; };
const parseDetail = (r) => { try { const d = JSON.parse(r.detail); return d && typeof d === 'object' ? d : null; } catch (e) { return null; } };
const arr = (x) => (x === undefined || x === null ? [] : Array.isArray(x) ? x : [x]); // ConvertTo-Json unwraps single-element arrays
const known = new Set(['all', 'chrome', 'edge']);
const unexpectedSteps = new Set();
let anyFail = false;
let imageCount = 0, imageBytes = 0;
const isObj = (x) => x !== null && typeof x === 'object' && !Array.isArray(x);
const validSelection = (items) => {
  items = arr(items);
  if (items.length !== 3) return false;
  // Validate EVERY item (selected and unselected) before any counting or comparison. No assumption about normal PowerShell output shape.
  for (const i of items) {
    if (!isObj(i)) return false;
    if (typeof i.selected !== 'boolean') return false;
    if (typeof i.hwnd !== 'number' || !Number.isSafeInteger(i.hwnd) || i.hwnd <= 0) return false;
    if (typeof i.title !== 'string') return false;
  }
  const hw = new Set(items.map((i) => i.hwnd));
  if (hw.size !== 2) return false;
  const letters = new Set(items.map((i) => i.title.split(/\s/)[0]));
  if (letters.size !== 3 || ![...letters].every((t) => /^StartAppFixture[ABC]$/.test(t))) return false;
  for (const h of hw) if (items.filter((i) => i.hwnd === h && i.selected === true).length !== 1) return false;
  return true;
};
const canon = (items) => JSON.stringify(arr(items).map((i) => [i.hwnd, i.title, i.selected]).sort());
{ // always evaluated: empty or missing matrix must be INCONCLUSIVE
  if (pick('all', 'preflight').length) note('all/preflight present (script stopped before observation)');
  for (const s of ['scope', 'complete']) {
    const r = pick('all', s);
    if (r.length !== 1 || r[0].status !== 'INFO') note(`all/${s} must be exactly one INFO row (count ${r.length})`);
  }
  for (const r of records) {
    if (!r) { note(`unexpected browser value ${r && r.browser}`); continue; }
    if (r.status === 'FAIL') anyFail = true; // FAIL precedence covers EVERY record, including unknown/missing browser identity; the identity reason below is still recorded.
    if (!known.has(r.browser)) { note(`unexpected browser value ${r.browser}`); continue; }
    if (!okStatus.has(r.status)) note(`invalid status ${r.status} on ${r.browser}/${r.step}`);
    if (r.browser === 'all') { if (!['preflight', 'scope', 'complete'].includes(r.step)) unexpectedSteps.add(`all/${r.step}`); }
    else if (!required.includes(r.step) && !optional.includes(r.step)) unexpectedSteps.add(`${r.browser}/${r.step}`);
  }
  for (const u of unexpectedSteps) note(`unexpected step ${u}`);
  for (const b of ['chrome', 'edge']) {
    for (const s of required) {
      const r = pick(b, s);
      if (r.length !== 1) { note(`${b}/${s} count ${r.length}`); continue; }
      const x = r[0].status;
      if (s === 'complete') { if (x !== 'INFO') note(`${b}/complete status ${x} not INFO`); }
      else if (s === 'version') {
        if (!['INFO', 'UNKNOWN', 'UNAVAILABLE'].includes(x)) note(`${b}/version status ${x} invalid`);
        if (x === 'INFO' && !String(r[0].detail || '').trim()) note(`${b}/version INFO with empty detail`);
      } else if (s === 'post-disable-unavailable') {
        if (x !== 'UNKNOWN') note(`${b}/post-disable-unavailable must be UNKNOWN in this scope, got ${x}`);
      } else if (!phaseStatuses.includes(x)) note(`${b}/${s} status ${x} not in ${phaseStatuses}`);
    }
    for (const s of optional) {
      const r = pick(b, s);
      if (r.length > 1) note(`${b}/${s} duplicated ${r.length}`);
      if (r.length === 1 && !phaseStatuses.includes(r[0].status)) note(`${b}/${s} optional status ${r[0].status} not in ${phaseStatuses}`);
    }
    // navigation PASS: exact intended URI and omnibox, author-confirmed boolean (not an independent document readback here).
    const nav = pick(b, 'navigation')[0];
    if (nav && nav.status === 'PASS') {
      const d = parseDetail(nav);
      if (!d || d.confirmed !== true || d.intended !== intendedUri[b] || d.omnibox !== intendedUri[b]) note(`${b} navigation PASS detail does not show confirmed exact intended URI and omnibox`);
    }
    // image PASS rows: required detail, exact local file, hash/size/PNG header integrity. Integrity only, not pixel validity.
    let perBrowser = 0;
    for (const p of imagePhases) {
      const r = pick(b, p)[0];
      if (!r || r.status !== 'PASS') continue;
      perBrowser++; imageCount++;
      const d = parseDetail(r);
      if (!d || !Number.isInteger(d.bytes) || !/^[0-9a-f]{64}$/.test(d.sha256 || '') || !Number.isInteger(d.width) || !Number.isInteger(d.height)) { note(`${b}/${p} image PASS detail lacks bytes/sha256/width/height`); continue; }
      if (d.bytes <= 0 || d.bytes > 262144 || d.width < 300 || d.width > 1600 || d.height < 200 || d.height > 1100) { note(`${b}/${p} image detail outside approved caps`); continue; }
      imageBytes += d.bytes;
      const f = join(dir, `${b}-${p}.png`);
      if (!existsSync(f)) { note(`${b}/${p} image PASS but PNG file missing`); continue; }
      const buf = readFileSync(f);
      if (buf.length !== d.bytes || createHash('sha256').update(buf).digest('hex') !== d.sha256) note(`${b}/${p} PNG size or hash differs from record`);
      else if (buf.length < 24 || buf.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a' || buf.readUInt32BE(16) !== d.width || buf.readUInt32BE(20) !== d.height) note(`${b}/${p} PNG header or dimensions differ from record`);
    }
    if (perBrowser > 7) note(`${b} has ${perBrowser} image PASS rows, over 7 per browser`);
    if (st(b, 'first-prompt') === 'PASS' && st(b, 'first-unattributed') === 'PASS') note(`${b} first-prompt and first-unattributed both PASS (mutually exclusive)`);
    if (st(b, 'reconnect-deny-prompt') === 'PASS' && st(b, 'reconnect-deny-unattributed') === 'PASS') note(`${b} reconnect-deny prompt and unattributed both PASS (mutually exclusive)`);
    // dependency order: later PASS cannot stand on an earlier non-PASS.
    const chain = [['inspect-toggle', ['navigation', 'fixture']], ['endpoint', ['inspect-toggle']], ['first-consent', ['endpoint']], ['enumeration', ['endpoint']], ['fixture-selection-unchanged', ['fixture']], ['reconnect-denied', ['enumeration']]];
    for (const [s, deps] of chain) if (st(b, s) === 'PASS') for (const dep of deps) if (st(b, dep) !== 'PASS') note(`${b}/${s} PASS but ${dep} is ${st(b, dep)}`);
    // consent: identified prompt plus simulated Allow, kept separate from enumeration.
    if (st(b, 'first-consent') === 'PASS' && (st(b, 'first-prompt') !== 'PASS' || pick(b, 'first-unattributed').length && st(b, 'first-unattributed') === 'PASS')) note(`${b}/first-consent PASS lacks a PASS first-prompt image row (or has first-unattributed PASS)`);
    // enumeration PASS: strict helper detail.
    const en = pick(b, 'enumeration')[0];
    if (en && en.status === 'PASS') {
      const d = parseDetail(en); const res = d && d.result; const t = res ? arr(res.targets) : [];
      const titles = new Set(t.map((x) => x && x.title)); const urls = new Set(t.map((x) => x && x.url));
      if (!d || d.helperExit !== 0 || !res || res.state !== 'enumerated' || t.length !== 3 || titles.size !== 3 || urls.size !== 3 ||
        !['StartAppFixtureA', 'StartAppFixtureB', 'StartAppFixtureC'].every((x) => titles.has(x)) || !t.every((x) => /^file:\/\//.test(String(x && x.url)))) note(`${b}/enumeration PASS detail is not helperExit 0, state enumerated and exactly 3 distinct known fixture titles with file:// URLs`);
    }
    // selection: PASS needs identical valid snapshots; FAIL needs two valid differing snapshots.
    const sel = pick(b, 'fixture-selection-unchanged')[0];
    if (sel && ['PASS', 'FAIL'].includes(sel.status)) {
      const d = parseDetail(sel);
      const valid = d && validSelection(d.before) && validSelection(d.after);
      const same = valid && canon(d.before) === canon(d.after);
      if (sel.status === 'PASS' && !same) note(`${b}/fixture-selection-unchanged PASS but snapshots not valid and identical`);
      if (sel.status === 'FAIL' && (!valid || same)) note(`${b}/fixture-selection-unchanged FAIL but snapshots not valid and different`);
    }
    // denial: PASS needs every author predicate re-derived from detail.
    const rd = pick(b, 'reconnect-denied')[0];
    if (rd && rd.status === 'PASS') {
      const d = parseDetail(rd); const res = d && d.result;
      if (!d || d.helperExit !== 7 || !res || res.state !== 'closed' || res.clean !== true || d.promptSeen !== true || d.decisionApplied !== true || d.decision !== 'deny') note(`${b}/reconnect-denied PASS detail is not prompt+deny+helperExit 7+closed+clean true`);
      if (st(b, 'reconnect-deny-prompt') !== 'PASS') note(`${b}/reconnect-denied PASS lacks a PASS reconnect-deny-prompt image row`);
    }
  }
  if (imageCount > 14) note(`${imageCount} image PASS rows, over 14 total`);
  if (imageBytes > 3.5 * 1024 * 1024) note(`image bytes ${imageBytes} over 3.5MiB`);
}
const nonPassPhase = records.filter((r) => r && r.browser !== 'all' && r.step !== 'post-disable-unavailable' && ['UNKNOWN', 'UNAVAILABLE'].includes(r.status));
const anyBad = records.some((r) => r && ['UNKNOWN', 'UNAVAILABLE', 'FAIL'].includes(r.status));
if (Number.isInteger(childExit)) {
  if (childExit === 0 && anyBad) note('contradiction: child exit 0 but non-PASS records');
  if (childExit === 2 && records.length && !anyBad) note('contradiction: child exit 2 but all records PASS/INFO');
  if (childExit !== 0 && childExit !== 2) note(`child exit ${childExit} is neither 0 nor 2`);
  if (childExit === 0 && !anyBad && records.length) note('child exit 0 is impossible for the full matrix (post-disable-unavailable is always UNKNOWN)');
}
let verdict;
if (anyFail) verdict = 'TEST_FAILURE';
else if (problems.length || nonPassPhase.length) verdict = 'INCONCLUSIVE';
else verdict = 'EVIDENCE_COLLECTED';
console.log(`OBS_VERDICT ${verdict} childExit=${exitArg} reasons=${problems.length} nonPassPhases=${nonPassPhase.length}`);
for (const p of problems) console.log(`OBS_REASON ${p}`);
for (const b of ['chrome', 'edge']) console.log(`OBS_PHASES ${b} ` + required.map((s) => `${s}=${st(b, s) ?? 'MISSING'}`).join(','));
for (const b of ['chrome', 'edge']) console.log(`OBS_OPTIONAL ${b} ` + optional.map((s) => `${s}=${st(b, s) ?? 'absent'}`).join(','));
console.log('OBS_NOTE FULL_GATE NOT_PASS by design (post-disable UNKNOWN). Per-phase results are independent: enumeration may PASS while consent stays UNKNOWN. Actual decoded pixels (executor, then requester main) are mandatory before any diagnosis. Simulated modal input is not real-user consent proof.');
process.exit(verdict === 'TEST_FAILURE' ? 1 : 0);
