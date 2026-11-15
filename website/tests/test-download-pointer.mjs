"use strict";
/* Offline tests for website/download-pointer.js. No network, no deps:
 * node website/tests/test-download-pointer.mjs */
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import assert from "node:assert/strict";
const require = createRequire(import.meta.url);
const P = require("../download-pointer.js");

const VALID = {
  schema: 1,
  channel: "prototype",
  label: "אב-טיפוס להערכה - לא גרסה יציבה",
  version: "0.1.0-proto.3",
  tag: "v0.1.0-proto.3",
  commit: "580bf649e411377361aa8fc075eebf4c0c3cd372",
  asset: "StartApp-win-x64.zip",
  url: "https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.3/StartApp-win-x64.zip",
  sha256: "a".repeat(64),
  size: 65162878,
  generated_utc: "2026-11-15T00:00:00Z",
  scope: "Declared release metadata only.",
};

let passed = 0;
function t(name, fn) {
  fn();
  passed++;
  console.log("PASS", name);
}

t("valid pointer validates", () => {
  const r = P.validate(VALID);
  assert.equal(r.state, "ok");
  assert.equal(r.pointer.version, "0.1.0-proto.3");
  assert.equal(r.pointer.url, VALID.url);
});

t("null candidate is a normal none state", () => {
  const r = P.validate({ schema: 1, channel: "prototype", candidate: null, reason: "no published prerelease with ui_gate=passed exists" });
  assert.equal(r.state, "none");
});

t("non-object rejected", () => {
  assert.equal(P.validate(null).state, "invalid");
  assert.equal(P.validate("x").state, "invalid");
  assert.equal(P.validate([VALID]).state, "invalid");
});

t("wrong schema rejected", () => {
  assert.equal(P.validate({ ...VALID, schema: 2 }).state, "invalid");
});

t("non-prototype channel rejected", () => {
  assert.equal(P.validate({ ...VALID, channel: "stable" }).state, "invalid");
});

t("bad version shapes rejected", () => {
  for (const v of ["0.1.0", "0.1.0-proto", "0.1.0-proto.x", "v0.1.0-proto.3", "1.0-proto.1"]) {
    assert.equal(P.validate({ ...VALID, version: v, tag: "v" + v }).state, "invalid", v);
  }
});

t("tag/version mismatch rejected", () => {
  assert.equal(P.validate({ ...VALID, tag: "v0.1.0-proto.4" }).state, "invalid");
});

t("non-https URL rejected; no URL ever invented", () => {
  assert.equal(P.validate({ ...VALID, url: "http://example.com/x.zip" }).state, "invalid");
  assert.equal(P.validate({ ...VALID, url: "" }).state, "invalid");
  const r = P.validate({ ...VALID, url: undefined });
  assert.equal(r.state, "invalid");
  assert.ok(!("pointer" in r));
});

t("bad commit rejected", () => {
  assert.equal(P.validate({ ...VALID, commit: "580bf64" }).state, "invalid");
  assert.equal(P.validate({ ...VALID, commit: "Z".repeat(40) }).state, "invalid");
});

t("wrong asset name rejected", () => {
  assert.equal(P.validate({ ...VALID, asset: "other.zip" }).state, "invalid");
});

t("sha256 null allowed, bad sha256 rejected", () => {
  assert.equal(P.validate({ ...VALID, sha256: null }).state, "ok");
  assert.equal(P.validate({ ...VALID, sha256: "A".repeat(64) }).state, "invalid");
  assert.equal(P.validate({ ...VALID, sha256: "a".repeat(63) }).state, "invalid");
});

t("size null allowed, bad sizes rejected", () => {
  assert.equal(P.validate({ ...VALID, size: null }).state, "ok");
  assert.equal(P.validate({ ...VALID, size: 0 }).state, "invalid");
  assert.equal(P.validate({ ...VALID, size: 1.5 }).state, "invalid");
});

t("URL pinned to this repo's release asset path", () => {
  assert.equal(P.validate({ ...VALID, url: "https://github.com/other/repo/releases/download/v0.1.0-proto.3/StartApp-win-x64.zip" }).state, "invalid");
  assert.equal(P.validate({ ...VALID, url: "https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.9/StartApp-win-x64.zip" }).state, "invalid");
  assert.equal(P.validate({ ...VALID, url: "https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.3/other.zip" }).state, "invalid");
  assert.equal(P.validate({ ...VALID, url: "https://evil.example/releases/download/v0.1.0-proto.3/StartApp-win-x64.zip" }).state, "invalid");
});

// --- render(): DOM-less doubles, mirroring the button contract ---
function fakeEls() {
  return {
    button: {
      disabled: true,
      textContent: "הורדה ל־Windows - עדיין לא זמינה",
      onclick: null,
    },
    note: { textContent: "" },
  };
}

t("render ok enables button and binds the validated URL only", () => {
  const els = fakeEls();
  P.render(els, P.validate(VALID));
  assert.equal(els.button.disabled, false);
  assert.match(els.button.textContent, /v0\.1\.0-proto\.3/);
  assert.equal(typeof els.button.onclick, "function");
  assert.match(els.note.textContent, /sha256/);
});

t("ok label and note isolate LTR data inside the RTL copy", () => {
  const els = fakeEls();
  P.render(els, P.validate(VALID));
  assert.ok(els.button.textContent.includes("\u2066v0.1.0-proto.3\u2069"), "tag isolated in label");
  assert.ok(els.note.textContent.includes("\u20660.1.0-proto.3\u2069"), "version isolated in note");
  assert.ok(els.note.textContent.includes("\u2066sha256: " + "a".repeat(64) + "\u2069"), "hash isolated in note");
});

t("repeated render(ok) replaces the handler, never accumulates", () => {
  const els = fakeEls();
  P.render(els, P.validate(VALID));
  const first = els.button.onclick;
  const newer = P.validate({ ...VALID, version: "0.1.0-proto.4", tag: "v0.1.0-proto.4", url: "https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.4/StartApp-win-x64.zip" });
  P.render(els, newer);
  assert.equal(typeof els.button.onclick, "function");
  assert.notEqual(els.button.onclick, first);
  assert.match(els.button.textContent, /v0\.1\.0-proto\.4/);
});

t("render non-ok after ok disables AND unbinds the stale URL", () => {
  const els = fakeEls();
  P.render(els, P.validate(VALID));
  P.render(els, { state: "none", reason: "withdrawn" });
  assert.equal(els.button.disabled, true);
  assert.equal(els.button.onclick, null);
  assert.equal(els.button.textContent, "אין הורדה זמינה כרגע", "label no longer claims the stale tag");
});

t("render none keeps disabled with truthful label and honest note", () => {
  const els = fakeEls();
  P.render(els, { state: "none", reason: "r" });
  assert.equal(els.button.disabled, true);
  assert.equal(els.button.onclick, null);
  assert.equal(els.button.textContent, "אין הורדה זמינה כרגע", "truthful none label");
  assert.ok(els.note.textContent.length > 0);
});

t("label adapter: rich CTA markup is never flattened", () => {
  const els = fakeEls();
  els.label = { textContent: "" };
  const originalButtonText = els.button.textContent;
  P.render(els, P.validate(VALID));
  assert.match(els.label.textContent, /v0\.1\.0-proto\.3/);
  assert.equal(els.button.textContent, originalButtonText, "button markup/text untouched when a label element is given");
});

t("navigation: clicking the enabled button assigns the validated URL", () => {
  const calls = [];
  globalThis.location = { assign: (u) => calls.push(u) };
  try {
    const els = fakeEls();
    P.render(els, P.validate(VALID));
    els.button.onclick();
    assert.deepEqual(calls, [VALID.url]);
  } finally {
    delete globalThis.location;
  }
});

t("load maps a non-JSON response to invalid, never throws", async () => {
  const realFetch = globalThis.fetch;
  globalThis.fetch = async () => ({ ok: true, status: 200, json: async () => { throw new SyntaxError("not json"); } });
  try {
    const r = await P.load("https://example.invalid/pointer.json");
    assert.equal(r.state, "invalid");
  } finally {
    globalThis.fetch = realFetch;
  }
});

t("load validates a well-formed JSON response", async () => {
  const realFetch = globalThis.fetch;
  globalThis.fetch = async () => ({ ok: true, status: 200, json: async () => VALID });
  try {
    const r = await P.load("https://example.invalid/pointer.json");
    assert.equal(r.state, "ok");
    assert.equal(r.pointer.url, VALID.url);
  } finally {
    globalThis.fetch = realFetch;
  }
});

t("load maps HTTP error status to invalid", async () => {
  const realFetch = globalThis.fetch;
  globalThis.fetch = async () => ({ ok: false, status: 404, json: async () => ({}) });
  try {
    const r = await P.load("https://example.invalid/pointer.json");
    assert.equal(r.state, "invalid");
  } finally {
    globalThis.fetch = realFetch;
  }
});

t("render invalid keeps disabled and leaks no document data", () => {
  const els = fakeEls();
  P.render(els, P.validate({ ...VALID, url: "http://evil.example/x" }));
  assert.equal(els.button.disabled, true);
  assert.equal(els.button.textContent, "הורדה לא זמינה", "truthful invalid label");
  assert.ok(!els.note.textContent.includes("evil.example"));
  assert.ok(!els.button.textContent.includes("evil.example"));
});

t("load rejects non-https pointer URL without fetching", async () => {
  const r = await P.load("http://example.com/pointer.json");
  assert.equal(r.state, "invalid");
});

t("load maps fetch failure to invalid, never throws", async () => {
  const r = await P.load("https://127.0.0.1:1/pointer.json");
  assert.equal(r.state, "invalid");
});

/* Wiring assertions: the activated page must load the reader and the wire
 * script, allow same-origin fetch in CSP, and resolve pointer.json relative
 * to the page (same-origin, https on the deployed site). */
const SITE = join(dirname(fileURLToPath(import.meta.url)), "..");
const INDEX = readFileSync(join(SITE, "index.html"), "utf8");
const WIRE = readFileSync(join(SITE, "download-wire.js"), "utf8");

t("index CSP allows same-origin fetch, nothing more", () => {
  const m = INDEX.match(/connect-src ([^;]+);/);
  assert.ok(m, "connect-src directive present");
  assert.equal(m[1], "'self'");
});

t("index loads reader then wire script", () => {
  const iReader = INDEX.indexOf('src="download-pointer.js"');
  const iWire = INDEX.indexOf('src="download-wire.js"');
  assert.ok(iReader > -1, "reader script tag present");
  assert.ok(iWire > iReader, "wire script loads after reader");
});

t("wire resolves pointer.json relative to the page (same-origin)", () => {
  assert.ok(WIRE.includes('new URL("pointer.json", window.location.href)'));
  assert.ok(WIRE.includes("StartAppPointer.load(url)"));
  assert.ok(!WIRE.includes("github.com"), "wire must not hardcode any origin");
});

t("wire renders both CTAs through the reader contract", () => {
  assert.equal((WIRE.match(/StartAppPointer.render/g) || []).length, 2);
  assert.ok(WIRE.includes("hero-download-state"));
  assert.ok(WIRE.includes("download-state"));
});

t("wire resolves state-bearing static copy (badge + heading) per state", () => {
  assert.ok(INDEX.includes('id="availability-badge"'), "badge id in index");
  assert.ok(INDEX.includes('id="download-heading"'), "heading id in index");
  assert.ok(WIRE.includes("availability-badge"), "wire updates badge");
  assert.ok(WIRE.includes("download-heading"), "wire updates heading");
  for (const state of ["ok", "none", "invalid"]) {
    assert.ok(WIRE.includes(state + ":"), "wire has " + state + " texts");
  }
});

t("state notes wrap over-long tokens instead of overflowing the layout", () => {
  const css = readFileSync(join(SITE, "styles.css"), "utf8");
  assert.ok(/\.hero-download-state \{[^}]*overflow-wrap: anywhere/.test(css), "hero note wraps long tokens");
  assert.ok(/\.note \{[^}]*overflow-wrap: anywhere/.test(css), "section note wraps long tokens");
});

t("static copy is state-neutral or truthful in every initial state", () => {
  const desc = INDEX.match(/name="description"\s+content="([^"]*)"/);
  assert.ok(desc, "meta description present");
  assert.ok(!desc[1].includes("הורדה פעילה"), "meta is state-neutral");
  // Initial (loading) copy must be the truthful "checking" state, not a claim.
  const checking = "בודק זמינות הורדה";
  assert.ok(INDEX.includes(checking), "initial CTA copy is the checking state");
  assert.ok(INDEX.includes("בודק אם קיימת גרסת אב-טיפוס מאומתת"),
    "initial note copy is the checking state");
  assert.ok(!INDEX.includes("<h2>אין הורדה זמינה כרגע.</h2>"),
    "static heading does not pre-claim 'none'");
});

console.log(passed + " tests passed");
