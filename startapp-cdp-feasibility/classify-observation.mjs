// Strict completeness classifier for observe-inspect.ps1 output. Plain Node, no dependencies.
// Usage: node classify-observation.mjs <out-dir> <child-exit-code>
// Verdicts: FAIL (exit 1), INCONCLUSIVE (exit 0), EVIDENCE_COLLECTED (exit 0).
// None of them is a feasibility/consent PASS. A green job is not browser success.
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
const [dir, exitArg] = process.argv.slice(2);
const childExit = /^[0-9]+$/.test(String(exitArg ?? '')) ? Number(exitArg) : NaN; // strict nonempty digits only
const problems = [];
let fail = false;
const note = (m) => problems.push(m);
let records = [];
try {
  let parsed = JSON.parse(readFileSync(join(dir, 'assertions.json'), 'utf8').replace(/^\uFEFF/, ''));
  if (!Array.isArray(parsed)) parsed = [parsed];
  records = parsed;
} catch (e) { note('INFRA assertions.json missing or unparseable'); }
if (!Number.isInteger(childExit)) note('INFRA child exit code missing');
const pick = (b, s) => records.filter((r) => r && r.browser === b && r.step === s);
const once = (b, s, allowed) => {
  const r = pick(b, s);
  if (r.length !== 1) { note(`${b}/${s} count ${r.length}`); return null; }
  if (!allowed.includes(r[0].status)) { note(`${b}/${s} status ${r[0].status} not in ${allowed}`); return r[0]; }
  return r[0];
};
const phaseStatuses = ['PASS', 'UNKNOWN', 'UNAVAILABLE', 'FAIL'];
{ // always evaluated: empty or missing matrix must be INCONCLUSIVE
  if (pick('all', 'preflight').length) note('all/preflight present (script stopped before observation)');
  once('all', 'scope', ['INFO']);
  once('all', 'complete', ['INFO']);
  for (const b of ['chrome', 'edge']) {
    once(b, 'version', ['INFO', 'UNKNOWN', 'UNAVAILABLE']);
    for (const s of ['window', 'navigation', 'document', 'uia', 'pixels', 'cleanup']) once(b, s, phaseStatuses);
    once(b, 'complete', ['INFO']);
    const v = pick(b, 'version')[0];
    if (v && v.status === 'INFO' && !String(v.detail || '').trim()) note(`${b}/version INFO with empty detail`);
  }
  const known = new Set(['all', 'chrome', 'edge']);
  for (const r of records) if (!r || !known.has(r.browser)) note(`unexpected browser value ${r && r.browser}`);
  const allowedSteps = new Set(['preflight', 'scope', 'complete', 'version', 'window', 'navigation', 'document', 'uia', 'pixels', 'cleanup', 'observation']);
  for (const r of records) if (!r || !allowedSteps.has(r.step)) note(`unexpected step ${r && r.step}`);
  const okStatus = new Set(['INFO', 'PASS', 'UNKNOWN', 'UNAVAILABLE', 'FAIL']);
  for (const r of records) if (!r || !okStatus.has(r.status)) note(`invalid status ${r && r.status} on ${r && r.browser}/${r && r.step}`);
  for (const r of records) if (r && r.status === 'FAIL') fail = true;
  const bad = records.filter((r) => r && ['UNKNOWN', 'UNAVAILABLE', 'FAIL'].includes(r.status));
  if (bad.length) note(`${bad.length} record(s) UNKNOWN/UNAVAILABLE/FAIL: ` + bad.map((r) => `${r.browser}/${r.step}=${r.status}`).join(','));
  // navigation PASS must carry detail JSON with confirmed===true and omnibox exactly equal to the intended URI for that browser.
  const intendedUri = { chrome: 'chrome://inspect/#remote-debugging', edge: 'edge://inspect/#remote-debugging' };
  for (const b of ['chrome', 'edge']) {
    const n = pick(b, 'navigation')[0];
    if (n && n.status === 'PASS') {
      try {
        const d = JSON.parse(n.detail);
        if (d.confirmed !== true || d.intended !== intendedUri[b] || d.omnibox !== intendedUri[b]) note(`${b} navigation PASS detail does not show exact intended URI and omnibox`);
      } catch (e) { note(`${b} navigation PASS detail unparseable`); }
    }
  }
  // pixels PASS must be backed by a PNG file whose hash matches the record.
  for (const b of ['chrome', 'edge']) {
    const p = pick(b, 'pixels')[0];
    if (p && p.status === 'PASS') {
      try {
        const d = JSON.parse(p.detail);
        const f = join(dir, `${b}-inspect.png`);
        if (!existsSync(f)) note(`${b} pixels PASS but PNG file missing`);
        else if (createHash('sha256').update(readFileSync(f)).digest('hex') !== d.sha256) note(`${b} PNG hash differs from record`);
      } catch (e) { note(`${b} pixels detail unparseable`); }
    }
  }
}
if (Number.isInteger(childExit)) {
  const bad = records.some((r) => r && ['UNKNOWN', 'UNAVAILABLE', 'FAIL'].includes(r.status));
  if (childExit === 0 && bad) note('contradiction: child exit 0 but non-PASS records');
  if (childExit === 2 && records.length && !bad) note('contradiction: child exit 2 but all records PASS/INFO');
  if (childExit !== 0 && childExit !== 2) note(`child exit ${childExit} is neither 0 nor 2`);
}
const verdict = fail ? 'FAIL' : problems.length ? 'INCONCLUSIVE' : 'EVIDENCE_COLLECTED';
console.log(`OBS_VERDICT ${verdict} childExit=${exitArg} reasons=${problems.length}`);
for (const p of problems) console.log(`OBS_REASON ${p}`);
console.log('OBS_NOTE Verdict is evidence-collection status only. Not a feasibility, consent or access-control PASS. Actual pixels and UIA must be inspected.');
process.exit(fail ? 1 : 0);
