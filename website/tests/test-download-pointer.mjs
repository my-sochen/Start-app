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
      textContent: "הורדה חינם - עדיין לא זמינה",
      onclick: null,
    },
    note: { textContent: "" },
  };
}

t("render ok enables button and binds the validated URL only", () => {
  const els = fakeEls();
  P.render(els, P.validate(VALID));
  assert.equal(els.button.disabled, false);
  assert.equal(els.button.textContent, "הורדה חינם");
  assert.equal(typeof els.button.onclick, "function");
  assert.match(els.note.textContent, /sha256/);
});

t("ok label and note isolate LTR data inside the RTL copy", () => {
  const els = fakeEls();
  P.render(els, P.validate(VALID));
  assert.equal(els.button.textContent, "הורדה חינם", "version stays out of primary action");
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
  assert.equal(els.button.textContent, "הורדה חינם");
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
  assert.equal(els.label.textContent, "הורדה חינם");
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
const DOCS = readFileSync(join(SITE, "docs.html"), "utf8");
const WIRE = readFileSync(join(SITE, "download-wire.js"), "utf8");

t("index and docs CSP allow same-origin fetch, nothing more", () => {
  for (const html of [INDEX, DOCS]) {
    const m = html.match(/connect-src ([^;]+);/);
    assert.ok(m, "connect-src directive present");
    assert.equal(m[1], "'self'");
  }
});

t("index loads reader then wire script", () => {
  const iReader = INDEX.indexOf('src="download-pointer.js"');
  const iWire = INDEX.indexOf('src="download-wire.js"');
  assert.ok(iReader > -1, "reader script tag present");
  assert.ok(iWire > iReader, "wire script loads after reader");
});

t("wire resolves pointer.json relative to the page (same-origin)", () => {
  assert.ok(WIRE.includes('document.documentElement.getAttribute("data-site-root")'));
  assert.ok(WIRE.includes('root + "pointer.json", window.location.href)'));
  assert.equal(new URL("../pointer.json", "https://example.test/Start-app/en/index.html").href, "https://example.test/Start-app/pointer.json");
  assert.ok(WIRE.includes("StartAppPointer.load(url)"));
  assert.ok(!WIRE.includes("github.com"), "wire must not hardcode any origin");
});

t("wire renders all download controls through the shared reader (home, guide, offer)", () => {
  assert.equal((WIRE.match(/StartAppPointer\.render\(/g) || []).length, 2);
  assert.ok(WIRE.includes("[data-download-cta]"));
  assert.equal((INDEX.match(/data-download-cta/g) || []).length, 1, "home has one CTA");
  assert.equal((DOCS.match(/data-download-cta/g) || []).length, 1, "guide has one gated CTA");
  for (const html of [INDEX, DOCS]) {
    assert.ok(!/<a[^>]*download-pointer|releases\/download/.test(html), "no static download URL in any page");
    const cta = html.match(/<button[^>]*data-download-cta[^>]*>/)[0];
    assert.ok(/\bdisabled\b/.test(cta), "CTA ships disabled (fail closed)");
  }
});

t("home is clean: guide/onboarding facts live only in the guide", () => {
  for (const s of ["StartApp.App.exe", "LOCALAPPDATA", "id=\"how\"", "class=\"faq\""]) {
    assert.ok(!INDEX.includes(s), "home has no " + s);
  }
  assert.ok(DOCS.includes("StartApp.App.exe") && DOCS.includes("חלצו את כל הקבצים"));
  assert.ok(DOCS.includes("לא נבדק באופן"), "guide states what was not verified");
});

t("wire resolves state-bearing static copy (badge + heading) per state", () => {
  assert.ok(!INDEX.includes('id="availability-badge"'), "no duplicate availability badge");
  assert.ok(WIRE.includes("availability-badge"), "wire stays null-safe for a badge");
  assert.ok(WIRE.includes("download-heading"), "wire updates heading");
  for (const state of ["ok", "none", "invalid"]) {
    assert.ok(WIRE.includes(state + ":"), "wire has " + state + " texts");
  }
});

t("pointer metadata wraps only the hash, never the version token", () => {
  const css = readFileSync(join(SITE, "styles.css"), "utf8");
  assert.ok(/\.ptr-sha \{[^}]*overflow-wrap: anywhere/.test(css), "hash token may wrap");
  assert.ok(/\.ptr-ltr \{[^}]*white-space: nowrap/.test(css), "version/size tokens never split");
  assert.ok(!/\.note \{[^}]*overflow-wrap/.test(css), "note element no longer breaks every token");
  assert.ok(!/\.hero-download-state \{[^}]*overflow-wrap/.test(css), "hero note no longer breaks every token");
});

t("public copy never claims a private review stage", () => {
  for (const html of [INDEX, DOCS]) {
    assert.ok(!html.includes("פרטית"), "no 'private' claim in public copy");
    assert.ok(html.includes("אב-טיפוס להערכה, לא גרסה יציבה"), "prototype truth is stated on the page (home near CTA, guide intro)");
    assert.ok(!html.includes("review-strip"), "no top unstable-version strip (owner steering)");
    assert.ok(!html.includes("בתכנון להפצה"), "stale planned-release wording removed");
  }
  assert.ok(!DOCS.includes("אין הורדה פעילה"), "docs makes no static download claim");
});

t("reader keeps technical version separate from the simple CTA", () => {
  const reader = readFileSync(join(SITE, "download-pointer.js"), "utf8");
  assert.ok(reader.includes('setLabel(tr("הורדה חינם"))'));
  assert.ok(reader.includes('mk("ptr-ltr", p.version)'));
  assert.ok(!reader.includes('tagSpan'));
});

t("docs carry no unconditional availability claim in either direction (regression)", () => {
  // The exact stale claims Moshe's pass caught must never come back:
  assert.ok(!DOCS.includes("ללא הורדה פעילה"), "docs meta: no 'no active download' claim");
  assert.ok(!DOCS.includes("אין כרגע חבילה זמינה להורדה"), "docs install: no 'no package available' claim");
  assert.ok(!DOCS.includes("אין הורדה פעילה"), "docs strip: no static download claim");
  // ...and the docs must not claim availability either: they defer to the
  // live homepage state, which is correct in both active and none states.
  const install = DOCS.slice(DOCS.indexOf('id="install"'), DOCS.indexOf('id="main-screen"'));
  assert.ok(install.includes('href="index.html#download"'), "install guidance links to the live homepage state");
  assert.ok(!/גרסה (מאומתת )?זמינה(?! להורדה כ)/.test(install.replace("כשקיימת גרסת אב-טיפוס מאומתת", "")),
    "install guidance makes no unconditional availability claim");
  assert.ok(!INDEX.includes("לסקירת אב-טיפוס") && !DOCS.includes("לסקירת אב-טיפוס"),
    "meta descriptions no longer describe the site as a private review");
});

t("hero CTA has distinct enabled vs disabled styling", () => {
  const css = readFileSync(join(SITE, "styles.css"), "utf8");
  const base = css.match(/\.hero-download-button \{([^}]*)\}/)[1];
  assert.ok(base.includes("var(--green)"), "enabled hero uses the design green");
  assert.ok(!base.includes("not-allowed"), "enabled hero is not styled disabled");
  assert.ok(/\.hero-download-button\[disabled\] \{[^}]*not-allowed/.test(css), "disabled hero keeps the disabled look");
});

t("shipped static copy is truthful with JavaScript disabled (no-JS fallback)", () => {
  const desc = INDEX.match(/name="description"\s+content="([^"]*)"/);
  assert.ok(desc, "meta description present");
  assert.ok(!desc[1].includes("הורדה פעילה"), "meta is state-neutral");
  // Without JavaScript no check can run: the shipped state must be the honest
  // "cannot verify - download stays off" copy, not a perpetual "checking".
  assert.ok(INDEX.includes("הורדה לא זמינה"), "static CTA is the disabled truth");
  assert.ok(INDEX.includes("לא ניתן לאמת כרגע גרסה להורדה"), "static note is the disabled truth");
  assert.ok(INDEX.includes("<noscript>"), "noscript explanation present");
  assert.ok(!INDEX.includes("<h2>אין הורדה זמינה כרגע.</h2>"),
    "static heading does not pre-claim 'none'");
});

t("wire upgrades to the checking state only when JavaScript runs", () => {
  assert.ok(WIRE.includes("applyLoading"), "wire has a loading upgrade");
  assert.ok(WIRE.includes("בודק זמינות הורדה"), "wire sets the checking label");
  assert.ok(WIRE.includes("בודק אם קיימת גרסת אב-טיפוס מאומתת"), "wire sets the checking note");
  const iStatic = INDEX.indexOf("הורדה לא זמינה");
  assert.ok(iStatic > -1, "checking copy must come from the wire, not the shipped HTML");
});

t("owner correction: regular-versions heading removed, Windows icon and word kept", () => {
  assert.ok(!INDEX.includes("<h4>גרסאות רגילות</h4>"), "regular-versions heading removed");
  assert.ok(INDEX.includes("אין במקור הנוכחי נתוני הורדה מאומתים לגרסאות רגילות."), "truthful regular-releases note kept");
  assert.ok(INDEX.includes('id="windows-download-title"') && INDEX.includes("<bdi>Windows</bdi>"), "Windows icon and word retained");
});

t("primary button restyle is scoped, light-blue over page, focus untouched", () => {
  const css = readFileSync(join(SITE, "styles.css"), "utf8");
  const base = css.match(/\.download-direct,\.doc-download button\.primary\{([^}]*)\}/)[1];
  assert.ok(base.includes("background:#e3edfc"), "light-blue surface slightly darker than page");
  assert.ok(base.includes('"Heebo"'), "button-scoped font change");
  assert.ok(base.includes("min-height:54px"), "usable button size kept");
  assert.ok(css.includes(".hero-download button:focus-visible,.hero-download summary:focus-visible,.doc-download button.primary:focus-visible{outline:3px solid #1b5de0;outline-offset:3px}"), "approved keyboard focus rule unchanged");
});

t("package-specific claims are identity-gated; design wording is the default", () => {
  assert.ok(DOCS.includes("data-layout-legacy-unqualified"), "current-project-design fallback present");
  assert.ok(/<p data-layout-legacy-qualification hidden>[\s\S]*?LOCALAPPDATA/.test(DOCS), "self-contained/no-sign-in/LOCALAPPDATA claims gated to the exact documented package");
  assert.ok(WIRE.includes("data-layout-legacy-unqualified"), "wire toggles the fallback copy");
});

t("loading never claims unavailable; render failure fails closed (review delta)", () => {
  assert.ok(WIRE.includes("renderOffers(null, true)"), "offers region shows checking while loading");
  assert.ok(WIRE.includes("function failClosed()"), "fail-closed render fallback present");
  assert.ok(WIRE.includes("} catch (_) { failClosed(); settle(); return; }"), "pointer render phase guarded and still signals settlement");
});

t("history controls carry version and tag in their accessible label", () => {
  const catalog = readFileSync(join(SITE, "download-catalog.js"), "utf8");
  for (const src of [WIRE, catalog]) {
    assert.ok(src.includes('setAttribute("aria-label"') || src.includes("setAttribute('aria-label'"), "row sets aria-label");
    assert.ok(src.includes("pointer.tag"), "label includes the exact tag");
  }
  assert.ok(!catalog.includes("Not loaded by HTML"), "stale inactive-consumer comment reconciled");
});

console.log(passed + " tests passed");
