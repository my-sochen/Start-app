// DOM behavior harness for the website download-pointer states.
// Self-contained: builds a temp site from ../../ (the website directory), serves it
// over local HTTPS, and drives real Chrome via puppeteer-core. See RUNBOOK.md.
import { execFileSync, spawn } from "node:child_process";
import { mkdtempSync, cpSync, existsSync, mkdirSync, readdirSync, statSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer from "puppeteer-core";

const HERE = dirname(fileURLToPath(import.meta.url));
const SITE_SRC = join(HERE, "..", "..");
const PORT = process.env.HARNESS_PORT || "8443";
const BASE = `https://127.0.0.1:${PORT}`;
const SHOTS = process.env.HARNESS_SCREENSHOTS || join(HERE, "screenshots");
const CHROME = process.env.HARNESS_CHROME || "/usr/bin/google-chrome";
const CERTS = join(HERE, "certs");

mkdirSync(SHOTS, { recursive: true });
if (!existsSync(join(CERTS, "cert.pem"))) {
  mkdirSync(CERTS, { recursive: true });
  execFileSync("openssl", ["req", "-x509", "-newkey", "rsa:2048", "-nodes",
    "-keyout", join(CERTS, "key.pem"), "-out", join(CERTS, "cert.pem"),
    "-days", "2", "-subj", "/CN=127.0.0.1",
    "-addext", "subjectAltName=IP:127.0.0.1"]);
}

// Temp site: website files + fixtures (repo tree stays clean).
const SITE = mkdtempSync(join(tmpdir(), "dom-harness-site-"));
for (const name of readdirSync(SITE_SRC)) {
  if (name === "tests") continue;
  const src = join(SITE_SRC, name);
  if (statSync(src).isFile()) cpSync(src, join(SITE, name));
}
cpSync(join(HERE, "fixtures"), join(SITE, "fixtures"), { recursive: true });

const server = spawn(process.execPath, [join(HERE, "server.js")], {
  env: { ...process.env, SITE_ROOT: SITE, CERT_DIR: CERTS, PORT },
  stdio: "ignore",
});
process.on("exit", () => { server.kill(); rmSync(SITE, { recursive: true, force: true }); });
await new Promise((r) => setTimeout(r, 800));

const RESOLVED_BADGES = ["גרסת אב-טיפוס זמינה להורדה", "אין עדיין גרסה מאומתת להורדה", "לא ניתן לאמת הורדה כרגע"];
const EXPECTED_URL = "https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.3/StartApp-win-x64.zip";
let failures = 0;
const ok = (cond, name) => { console.log((cond ? "PASS " : "FAIL ") + name); if (!cond) failures++; };

function setFixture(state) {
  if (state === "missing") rmSync(join(SITE, "pointer.json"), { force: true });
  else cpSync(join(SITE, "fixtures", `${state}.json`), join(SITE, "pointer.json"));
}

const browser = await puppeteer.launch({
  executablePath: CHROME,
  headless: "new",
  args: ["--no-sandbox", "--ignore-certificate-errors"],
});

async function loadState(state, viewport) {
  setFixture(state);
  const page = await browser.newPage();
  await page.setViewport(viewport);
  await page.goto(`${BASE}/`, { waitUntil: "domcontentloaded" });
  await page.waitForFunction(
    (badges) => {
      const b = document.getElementById("availability-badge");
      const note = document.getElementById("download-state");
      // resolved = badge on a final value AND note past both static and loading copy
      return b && note && badges.includes(b.textContent.trim()) &&
             !note.textContent.includes("בודק אם קיימת");
    },
    { timeout: 8000 },
    RESOLVED_BADGES
  );
  return page;
}

const grab = (page) => page.evaluate(() => {
  const hero = document.querySelector(".hero-download-button");
  const main = document.querySelector("#download button.primary");
  const hcs = getComputedStyle(hero);
  const mcs = getComputedStyle(main);
  const ptrLtr = document.querySelector("#download-state .ptr-ltr");
  return {
    overflowX: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    heroStyle: { cursor: hcs.cursor, bg: hcs.backgroundColor },
    mainStyle: { cursor: mcs.cursor, bg: mcs.backgroundColor },
    ptr: {
      hasLtr: !!ptrLtr,
      hasSha: !!document.querySelector("#download-state .ptr-sha"),
      ltrNoWrap: ptrLtr ? getComputedStyle(ptrLtr).whiteSpace === "nowrap" : null,
      ltrFits: ptrLtr ? ptrLtr.getBoundingClientRect().width <= document.documentElement.clientWidth : null,
    },
    badge: document.getElementById("availability-badge").textContent.trim(),
    heading: document.getElementById("download-heading").textContent.trim(),
    hero: { disabled: hero.disabled, onclickIsFn: typeof hero.onclick === "function",
            label: hero.querySelector("span").textContent.trim(),
            note: document.getElementById("hero-download-state").textContent.trim() },
    main: { disabled: main.disabled, onclickIsFn: typeof main.onclick === "function",
            label: main.textContent.trim(),
            note: document.getElementById("download-state").textContent.trim() },
  };
});

// ---- ok state ----
let page = await loadState("ok", { width: 1440, height: 900 });
let s = await grab(page);
ok(!s.overflowX, "ok desktop 1440: no horizontal overflow (scrollWidth <= clientWidth)");
ok(!s.hero.disabled && !s.main.disabled, "ok: both CTAs enabled");
ok(s.heroStyle.cursor === "pointer" && s.heroStyle.bg === "rgb(36, 87, 73)", "ok: hero CTA enabled styling (pointer + design green)");
ok(s.ptr.hasLtr && s.ptr.hasSha && s.ptr.ltrNoWrap && s.ptr.ltrFits, "ok: note metadata uses nowrap LTR spans that fit the viewport");
ok(s.hero.onclickIsFn && s.main.onclickIsFn, "ok: both CTAs have onclick handlers");
ok(s.hero.label === "הורדה ל־Windows - \u2066v0.1.0-proto.3\u2069", "ok: hero label carries isolated tag");
ok(s.main.label === "הורדה ל־Windows - \u2066v0.1.0-proto.3\u2069", "ok: main label carries isolated tag");
ok(s.main.note.includes("0.1.0-proto.3") && s.main.note.includes("sha256: " + "a".repeat(64)), "ok: note carries version+sha256 (scoped spans)");
ok(s.main.note.includes("הבייטים עצמם לא נבדקו מחדש"), "ok: note carries the no-byte-reverification honesty sentence");
ok(s.badge === "גרסת אב-טיפוס זמינה להורדה", "ok: badge truthful");
ok(s.heading === "גרסת אב-טיפוס זמינה.", "ok: heading truthful");

// screenshots BEFORE any click (clicks kill the tab): ok desktop + mobile
await page.screenshot({ path: join(SHOTS, "ok-desktop-1440.png"), fullPage: true });
await page.setViewport({ width: 390, height: 844 });
const sMobile = await grab(page);
ok(!sMobile.overflowX, "ok mobile 390: no horizontal overflow (scrollWidth <= clientWidth)");
ok(sMobile.ptr.ltrFits && sMobile.ptr.ltrNoWrap, "ok mobile 390: version token unbroken and fits");
await page.screenshot({ path: join(SHOTS, "ok-mobile-390.png"), fullPage: true });
await page.setViewport({ width: 320, height: 700 });
const s320 = await grab(page);
ok(!s320.overflowX, "ok 320: no horizontal overflow");
ok(s320.ptr.ltrFits && s320.ptr.ltrNoWrap, "ok 320: version token unbroken and fits");
await page.screenshot({ path: join(SHOTS, "ok-mobile-320.png"), fullPage: true });
await page.close();

// real click -> exact download target (intercept; each click kills its tab)
async function clickTarget(selector, name) {
  const p2 = await loadState("ok", { width: 1440, height: 900 });
  const hits = [];
  await p2.setRequestInterception(true);
  p2.on("request", (req) => {
    if (req.url().startsWith("https://github.com/")) { hits.push(req.url()); req.abort(); }
    else req.continue();
  });
  await p2.click(selector);
  await new Promise(r => setTimeout(r, 600));
  ok(hits.length === 1 && hits[0] === EXPECTED_URL, name);
  await p2.close();
}
await clickTarget("#download button.primary", "ok: main CTA click targets the exact pinned release URL");
await clickTarget(".hero-download-button", "ok: hero CTA click targets the exact pinned release URL");

// ---- none state (incl. withdrawal shape: candidate null) ----
page = await loadState("none", { width: 1440, height: 900 });
s = await grab(page);
ok(!s.overflowX, "none desktop 1440: no horizontal overflow");
ok(s.hero.disabled && s.main.disabled, "none: both CTAs disabled");
ok(s.heroStyle.cursor === "not-allowed" && s.heroStyle.bg === "rgb(85, 100, 93)", "none: hero CTA disabled styling (not-allowed + gray)");
ok(!s.ptr.hasLtr, "none: no pointer metadata rendered");
ok(!s.hero.onclickIsFn && !s.main.onclickIsFn, "none: no onclick handler (no stale URL)");
ok(s.main.note.includes("אין עדיין גרסת אב-טיפוס שעברה את כל הבדיקות"), "none: honest note");
ok(s.hero.label === "אין הורדה זמינה כרגע" && s.main.label === "אין הורדה זמינה כרגע", "none: both CTA labels truthful (not stuck on 'checking')");
ok(s.badge === "אין עדיין גרסה מאומתת להורדה", "none: badge truthful");
ok(s.heading === "אין הורדה זמינה כרגע.", "none: heading truthful");
await page.screenshot({ path: join(SHOTS, "none-desktop-1440.png"), fullPage: true });
await page.setViewport({ width: 390, height: 844 });
const sMobileNone = await grab(page);
ok(!sMobileNone.overflowX, "none mobile 390: no horizontal overflow");
await page.screenshot({ path: join(SHOTS, "none-mobile-390.png"), fullPage: true });
await page.setViewport({ width: 320, height: 700 });
const sNone320 = await grab(page);
ok(!sNone320.overflowX, "none 320: no horizontal overflow");
await page.screenshot({ path: join(SHOTS, "none-mobile-320.png"), fullPage: true });
await page.close();

// ---- invalid document ----
page = await loadState("invalid", { width: 1440, height: 900 });
s = await grab(page);
ok(s.hero.disabled && s.main.disabled, "invalid: both CTAs disabled");
ok(!s.hero.onclickIsFn && !s.main.onclickIsFn, "invalid: no onclick handler");
ok(s.main.note.includes("לא ניתן לאמת כרגע גרסה להורדה"), "invalid: honest note");
ok(s.hero.label === "הורדה לא זמינה" && s.main.label === "הורדה לא זמינה", "invalid: both CTA labels truthful");
ok(s.badge === "לא ניתן לאמת הורדה כרגע", "invalid: badge truthful");
ok(s.heading === "לא ניתן לאמת הורדה כרגע.", "invalid: heading truthful");
await page.close();

// ---- missing pointer file (404 -> fetch failure) ----
page = await loadState("missing", { width: 1440, height: 900 });
s = await grab(page);
ok(s.hero.disabled && s.main.disabled, "missing: both CTAs disabled");
ok(!s.hero.onclickIsFn && !s.main.onclickIsFn, "missing: no onclick handler");
ok(s.main.note.includes("לא ניתן לאמת כרגע גרסה להורדה"), "missing: honest note");
ok(s.hero.label === "הורדה לא זמינה" && s.main.label === "הורדה לא זמינה", "missing: both CTA labels truthful");
ok(s.badge === "לא ניתן לאמת הורדה כרגע", "missing: badge truthful");
await page.close();

// ---- no JavaScript: shipped static state must be the truthful disabled state ----
{
  const p3 = await browser.newPage();
  await p3.setJavaScriptEnabled(false);
  await p3.setViewport({ width: 1440, height: 900 });
  setFixture("ok");
  await p3.goto(`${BASE}/`, { waitUntil: "domcontentloaded" });
  const s = await p3.evaluate(() => {
    const hero = document.querySelector(".hero-download-button");
    const main = document.querySelector("#download button.primary");
    const ns = document.querySelector("#download noscript");
    return {
      heroDisabled: hero.disabled, mainDisabled: main.disabled,
      heroLabel: hero.querySelector("span").textContent.trim(),
      mainLabel: main.textContent.trim(),
      badge: document.getElementById("availability-badge").textContent.trim(),
      note: document.getElementById("download-state").textContent.trim(),
      noscript: ns ? ns.textContent.trim() : null,
    };
  });
  ok(s.heroDisabled && s.mainDisabled, "no-JS: both CTAs disabled even with a valid pointer present");
  ok(s.heroLabel === "הורדה לא זמינה" && s.mainLabel.includes("הורדה לא זמינה"), "no-JS: labels are the truthful disabled state, never stuck on 'checking'");
  ok(s.badge === "לא ניתן לאמת הורדה כרגע", "no-JS: badge truthful");
  ok(s.note.includes("לא ניתן לאמת כרגע גרסה להורדה"), "no-JS: note truthful");
  ok(s.noscript && s.noscript.includes("JavaScript כבוי"), "no-JS: noscript explanation rendered");
  await p3.close();
}

await browser.close();
execFileSync("rm", ["-f", "/tmp/site-fixture/pointer.json"]);
console.log(failures === 0 ? "ALL DOM-BEHAVIOR CHECKS PASSED" : failures + " FAILURES");
process.exit(failures ? 1 : 0);
