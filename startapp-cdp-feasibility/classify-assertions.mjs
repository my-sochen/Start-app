// Wrapper-lane independent classifier for the v3 synthetic consent diagnostic.
// Reads the probe's assertions.json plus the preserved raw child exit code and
// validates the v3 contract matrix. Never passes on PASS count alone.
//
// Verdicts:
//   COMPLETE_PASS  exact full matrix: every required phase PASS in chrome AND
//                  edge, version INFO nonempty, markers exactly once, child
//                  exit 0, no FAIL/UNKNOWN/UNAVAILABLE anywhere.
//   TEST_FAILURE   any FAIL record.
//   INCONCLUSIVE   missing/duplicate/invalid phase or marker, any
//                  UNKNOWN/UNAVAILABLE, nonzero child exit, exit/record
//                  contradiction.
//   INFRA          classification inputs missing or unparseable.
//
// Exit code: 0 for COMPLETE_PASS and INCONCLUSIVE, 1 for TEST_FAILURE and
// INFRA. A green CI job means the diagnostic ran and was classified; it is
// never substantive PASS. The VERDICT line is authoritative.
//
// Wrapper-strictness beyond the v3 contract minimum (flagged for review):
// requires the all/scope INFO marker exactly once in addition to the
// contract-required all/complete INFO marker. The v3 probe always emits both.

import fs from 'node:fs';

const [assertionsPath, exitCodePath] = process.argv.slice(2);

const BROWSERS = ['chrome', 'edge'];
const KNOWN_BROWSER_KEYS = new Set([...BROWSERS, 'all']);
const REQUIRED_PHASES = [
  'fixture',
  'inspect-toggle',
  'endpoint',
  'first-consent',
  'enumeration',
  'fixture-selection-unchanged',
  'reconnect-denied',
  'disable',
  'post-disable-unavailable',
  'cleanup',
];
const STATUSES = new Set(['PASS', 'FAIL', 'UNKNOWN', 'UNAVAILABLE', 'INFO']);

function finish(verdict, reasons) {
  console.log(`VERDICT=${verdict}`);
  for (const r of reasons) console.log(`- ${r}`);
  const annotation = verdict === 'COMPLETE_PASS' ? 'notice' : verdict === 'INCONCLUSIVE' ? 'warning' : 'error';
  console.log(`::${annotation}::Consent-probe verdict: ${verdict} (${reasons.length} reason(s)). Green CI is never substantive PASS.`);
  const summary = [
    '## Consent-probe classification',
    '',
    `**Verdict: ${verdict}**`,
    '',
    ...reasons.map((r) => `- ${r}`),
    '',
    'A green CI job means the diagnostic ran and was classified; it is never substantive PASS.',
    'v3 intentionally cannot produce a full consent/access-control gate PASS.',
  ].join('\n');
  if (process.env.GITHUB_STEP_SUMMARY) {
    try { fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary + '\n'); } catch {}
  }
  process.exit(verdict === 'TEST_FAILURE' || verdict === 'INFRA' ? 1 : 0);
}

// --- INFRA: inputs must exist and parse ---
if (!assertionsPath || !exitCodePath) {
  finish('INFRA', ['usage: node classify-assertions.mjs <assertions.json> <exit-code-file>']);
}

let raw;
try {
  raw = fs.readFileSync(assertionsPath, 'utf8');
} catch (e) {
  finish('INFRA', [`assertions.json unreadable: ${e.message}`]);
}
let records;
try {
  records = JSON.parse(raw);
} catch (e) {
  finish('INFRA', [`assertions.json is not valid JSON: ${e.message}`]);
}
if (!Array.isArray(records)) finish('INFRA', ['assertions.json root is not an array']);

let childExit;
try {
  const t = fs.readFileSync(exitCodePath, 'utf8').trim();
  if (!/^-?\d+$/.test(t)) throw new Error(`not an integer: ${JSON.stringify(t)}`);
  childExit = Number(t);
} catch (e) {
  finish('INFRA', [`child exit code file unreadable/invalid: ${e.message}`]);
}

const reasons = [];

// --- record schema ---
records.forEach((r, i) => {
  if (!r || typeof r !== 'object' || typeof r.browser !== 'string' || typeof r.step !== 'string' || !r.step) {
    reasons.push(`record #${i}: missing/invalid browser or step`);
    return;
  }
  if (!KNOWN_BROWSER_KEYS.has(r.browser)) reasons.push(`record #${i}: unknown browser key ${JSON.stringify(r.browser)}`);
  if (!STATUSES.has(r.status)) reasons.push(`record ${r.browser}/${r.step}: invalid status ${JSON.stringify(r.status)}`);
});

const failRecords = records.filter((r) => r && r.status === 'FAIL');
const nonPass = records.filter((r) => r && (r.status === 'UNKNOWN' || r.status === 'UNAVAILABLE'));

const get = (b, s) => records.filter((r) => r && r.browser === b && r.step === s);

// --- per-browser required matrix ---
for (const b of BROWSERS) {
  const v = get(b, 'version');
  if (v.length !== 1) {
    reasons.push(`${b}/version: expected exactly 1 record, found ${v.length}`);
  } else {
    if (!['INFO', 'UNKNOWN', 'UNAVAILABLE'].includes(v[0].status)) {
      reasons.push(`${b}/version: status ${v[0].status} not permitted for version`);
    }
    if (v[0].status === 'INFO' && !(typeof v[0].detail === 'string' && v[0].detail.trim())) {
      reasons.push(`${b}/version: INFO with empty version detail`);
    }
  }
  for (const p of REQUIRED_PHASES) {
    const c = get(b, p).length;
    if (c === 0) reasons.push(`${b}/${p}: missing required phase`);
    else if (c > 1) reasons.push(`${b}/${p}: duplicate required phase (${c} records)`);
    else if (get(b, p)[0].status === 'INFO') reasons.push(`${b}/${p}: INFO is not a permitted status for a required phase`);
  }
  const c = get(b, 'complete').length;
  if (c !== 1) reasons.push(`${b}/complete: expected exactly 1 INFO end marker, found ${c}`);
  else if (get(b, 'complete')[0].status !== 'INFO') reasons.push(`${b}/complete: status must be INFO`);
}

// --- global markers (all/complete per contract; all/scope wrapper-strict) ---
for (const s of ['scope', 'complete']) {
  const c = get('all', s).length;
  if (c !== 1) reasons.push(`all/${s}: expected exactly 1 INFO marker, found ${c}`);
  else if (get('all', s)[0].status !== 'INFO') reasons.push(`all/${s}: status must be INFO`);
}

// --- any UNKNOWN/UNAVAILABLE anywhere is inconclusive evidence ---
for (const r of nonPass) reasons.push(`${r.browser}/${r.step}: ${r.status} is inconclusive evidence, never PASS`);

// --- child exit semantics ---
if (childExit !== 0) {
  reasons.push(`probe child exit ${childExit} (nonzero): cannot be COMPLETE_PASS`);
} else if (failRecords.length || nonPass.length) {
  reasons.push('probe child exit 0 contradicts FAIL/UNKNOWN/UNAVAILABLE records: harness contract violation');
}

// --- verdict ---
if (failRecords.length) {
  finish('TEST_FAILURE', [
    `${failRecords.length} FAIL record(s):`,
    ...failRecords.map((r) => `${r.browser}/${r.step}: ${String(r.detail).slice(0, 200)}`),
    ...reasons,
  ]);
}
if (reasons.length) finish('INCONCLUSIVE', reasons);

// Defensive: COMPLETE_PASS requires every required phase PASS in both browsers.
for (const b of BROWSERS) {
  for (const p of REQUIRED_PHASES) {
    if (get(b, p)[0].status !== 'PASS') finish('INCONCLUSIVE', [`${b}/${p} is ${get(b, p)[0].status}, not PASS`]);
  }
  if (get(b, 'version')[0].status !== 'INFO') finish('INCONCLUSIVE', [`${b}/version is not INFO`]);
}
finish('COMPLETE_PASS', [
  'Exact v3 matrix complete: all required phases PASS in chrome and edge, version INFO nonempty, markers exactly once, child exit 0.',
]);
