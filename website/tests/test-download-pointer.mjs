"use strict";
/* Offline tests for website/download-pointer.js. No network, no deps:
 * node website/tests/test-download-pointer.mjs */
import { createRequire } from "node:module";
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
});

t("render none keeps disabled with honest note", () => {
  const els = fakeEls();
  P.render(els, { state: "none", reason: "r" });
  assert.equal(els.button.disabled, true);
  assert.equal(els.button.onclick, null);
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
  assert.ok(!els.note.textContent.includes("evil.example"));
});

t("load rejects non-https pointer URL without fetching", async () => {
  const r = await P.load("http://example.com/pointer.json");
  assert.equal(r.state, "invalid");
});

t("load maps fetch failure to invalid, never throws", async () => {
  const r = await P.load("https://127.0.0.1:1/pointer.json");
  assert.equal(r.state, "invalid");
});

console.log(passed + " tests passed");
