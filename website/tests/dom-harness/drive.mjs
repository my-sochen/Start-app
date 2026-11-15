// DOM behavior harness: download-pointer states on every CTA (home + guide) and
// no-full-reload navigation (history, scroll, focus, announcements, fallbacks).
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
  if (statSync(src).isFile() || name === "shots") cpSync(src, join(SITE, name), { recursive: true });
}
cpSync(join(HERE, "fixtures"), join(SITE, "fixtures"), { recursive: true });

// project-style subdirectory copy (like https://host/Start-app/) for base-path regressions
mkdirSync(join(SITE, "Start-app"));
for (const name of readdirSync(SITE)) {
  const src = join(SITE, name);
  if (name !== "Start-app" && name !== "fixtures" && statSync(src).isFile()) cpSync(src, join(SITE, "Start-app", name));
}
cpSync(join(SITE, "shots"), join(SITE, "Start-app", "shots"), { recursive: true });

const server = spawn(process.execPath, [join(HERE, "server.js")], {
  env: { ...process.env, SITE_ROOT: SITE, CERT_DIR: CERTS, PORT },
  stdio: "ignore",
});
process.on("exit", () => { server.kill(); rmSync(SITE, { recursive: true, force: true }); });
await new Promise((r) => setTimeout(r, 800));

const EXPECTED_URL = "https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.3/StartApp-win-x64.zip";
let failures = 0;
const ok = (cond, name) => { console.log((cond ? "PASS " : "FAIL ") + name); if (!cond) failures++; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function setFixture(state) {
  for (const dir of [SITE, join(SITE, "Start-app")]) {
    if (state === "missing") rmSync(join(dir, "pointer.json"), { force: true });
    else cpSync(join(SITE, "fixtures", `${state}.json`), join(dir, "pointer.json"));
  }
}
const browser = await puppeteer.launch({
  executablePath: CHROME, headless: "new",
  args: ["--no-sandbox", "--ignore-certificate-errors"],
});
async function open(path, state, vp, js = true) {
  setFixture(state);
  const page = await browser.newPage();
  if (!js) await page.setJavaScriptEnabled(false);
  await page.setViewport(vp);
  await page.goto(`${BASE}${path}`, { waitUntil: "domcontentloaded" });
  if (js) await settle(page);
  return page;
}
const settle = (page) => page.waitForFunction(
  () => {
    const b = document.querySelector("[data-download-cta]");
    const n = document.getElementById(b.getAttribute("aria-describedby"));
    return !n.textContent.includes("בודק אם קיימת");
  }, { timeout: 8000 });
const ctaState = (page) => page.evaluate(() => {
  const b = document.querySelector("[data-download-cta]");
  const label = b.textContent.trim();
  const note = document.getElementById(b.getAttribute("aria-describedby")).textContent.trim();
  const det = document.getElementById(b.getAttribute("data-verify"));
  const cs = getComputedStyle(b);
  return {
    disabled: b.disabled, onclick: typeof b.onclick === "function", label, note,
    detailHidden: det.parentNode.hidden, detailText: det.textContent,
    cursor: cs.cursor, overflow: document.documentElement.scrollWidth > innerWidth + 1,
    badge: undefined,
  };
});
async function clickCta(page) {
  await page.evaluate(() => {
    window.__assigned = null;
    const b = document.querySelector("[data-download-cta]");
    // location.assign cannot be stubbed in Chrome; observe via beforeunload-free probe
    window.__probe = b.onclick;
  });
  const nav = page.waitForNavigation({ timeout: 4000 }).catch(() => null);
  page.once("request", () => {});
  const urls = [];
  const h = (r) => urls.push(r.url());
  page.on("request", h);
  await page.evaluate(() => document.querySelector("[data-download-cta]").click());
  await nav; await sleep(200);
  page.off("request", h);
  return urls.some((u) => u === EXPECTED_URL);
}

const VIEWPORTS = [[1440, 900], [390, 844], [320, 700]];
for (const path of ["/", "/docs.html"]) {
  const tag = path === "/" ? "home" : "guide";
  for (const state of ["ok", "none", "invalid", "missing"]) {
    const page = await open(path, state, { width: 1440, height: 900 });
    const s = await ctaState(page);
    if (state === "ok") {
      ok(!s.disabled && s.onclick && s.cursor === "pointer", `${tag} ok: CTA enabled with handler`);
      ok(s.label.includes("v0.1.0-proto.3"), `${tag} ok: label carries tag`);
      ok(!s.detailHidden && s.detailText.includes("0.1.0-proto.3") && s.detailText.includes("sha256: " + "a".repeat(64)) && s.detailText.includes("הבייטים עצמם לא נבדקו מחדש"),
         `${tag} ok: version/sha/honesty inside the verification panel`);
      ok(!s.note.includes("sha256") && s.note.length < 90, `${tag} ok: visible note stays short`);
      ok(s.badge === undefined || s.badge === "גרסת אב-טיפוס זמינה להורדה", `${tag} ok: badge truthful`);
      ok(await clickCta(page), `${tag} ok: click targets the exact pinned release URL`);
    } else {
      ok(s.disabled && !s.onclick && s.cursor === "not-allowed", `${tag} ${state}: CTA disabled, no handler (fail closed)`);
      ok(s.detailHidden && s.detailText === "", `${tag} ${state}: verification panel hidden and empty`);
      const lab = state === "none" ? "אין הורדה זמינה כרגע" : "הורדה לא זמינה";
      ok(s.label === lab, `${tag} ${state}: truthful label`);
      ok(!/https?:\/\//.test(await page.evaluate(() => document.querySelector("[data-download-cta]").outerHTML)), `${tag} ${state}: no URL on the button`);
    }
    await page.close();
  }
  for (const [w, h] of VIEWPORTS) {
    const page = await open(path, "ok", { width: w, height: h });
    const s = await ctaState(page);
    ok(!s.overflow, `${tag} ${w}px: no horizontal overflow`);
    if (s.overflow) console.log("OVF", await page.evaluate(() => [...document.querySelectorAll("body *")].filter((e) => e.getBoundingClientRect().right > innerWidth + 1).slice(0, 5).map((e) => e.tagName + "." + e.className + ":" + Math.round(e.getBoundingClientRect().right))));
    const el = path === "/" ? null : await page.$("#install");
    const clip = await page.evaluate(() => {
      const bad = [...document.querySelectorAll("main h1,main h2,main h3,main p,main li,main button,main summary,.docs-jump a")].filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && (r.left < -0.5 || r.right > innerWidth + 0.5); });
      return bad.map((e) => e.tagName + ":" + e.textContent.trim().slice(0, 20));
    });
    ok(clip.length === 0, `${tag} ${w}px: no element extends past either viewport edge` + (clip.length ? " " + clip : ""));
    await page.evaluate(() => {
      const b = document.createElement("div");
      b.id = "fixture-label";
      b.textContent = "LOCAL FIXTURE / DUMMY pointer data (fake version and hash) - NOT real release availability";
      b.style.cssText = "position:fixed;left:0;right:0;bottom:0;z-index:99;background:#b00020;color:#fff;font:bold 12px Arial;padding:6px 10px;text-align:center;direction:ltr";
      document.body.appendChild(b);
    });
    if (el) {
      await el.evaluate((e) => e.scrollIntoView({ block: "start" }));
      await sleep(300);
      await page.screenshot({ path: join(SHOTS, `guide-install-${w}-native-collapsed.png`) });
    } else {
      await page.screenshot({ path: join(SHOTS, `home-${w}-native-collapsed.png`) });
      await page.evaluate(() => { document.querySelector("details.verify").open = true; });
      await page.screenshot({ path: join(SHOTS, `home-${w}-native-expanded.png`) });
    }
    const ov = await page.evaluate(() => [...document.querySelectorAll(".ptr-ltr")].every((e) => e.scrollWidth <= e.parentElement.clientWidth + 1));
    ok(ov, `${tag} ${w}px: version tokens fit`);
    await page.close();
  }
}

// ---- home facts: clean, no hero image, no duplicate badge ----
{
  const page = await open("/", "ok", { width: 1440, height: 900 });
  const f = await page.evaluate(() => {
    const hero = document.querySelector("[data-download-cta]").getBoundingClientRect();
    return { heroH: hero.height, sections: document.querySelectorAll("main section").length,
      imgs: document.querySelectorAll("main img").length,
      pageH: document.documentElement.scrollHeight, vh: innerHeight, ctaTop: hero.top, ctaBottom: hero.bottom };
  });
  ok(await page.$("#availability-badge") === null && await page.$(".review-strip") === null, "home has no top strip or duplicate badge");
  ok(await page.evaluate(() => { const p = document.querySelector(".proto-note"); const c = document.querySelector("[data-download-cta]").getBoundingClientRect(); const n = p.getBoundingClientRect(); return p.textContent.includes("לא גרסה יציבה") && Math.abs(n.top - c.bottom) < 120; }), "prototype truth sits near the CTA");
  ok(await page.evaluate(() => { const h = document.querySelector("main h1").getBoundingClientRect(); const c = document.querySelector("[data-download-cta]").getBoundingClientRect(); return Math.abs((h.left + h.right) / 2 - innerWidth / 2) < 40 && Math.abs((c.left + c.right) / 2 - innerWidth / 2) < 40; }), "product name and CTA are centered");
  ok(f.sections === 1, "home has a single hero section (no lower guide/onboarding)");
  ok(f.imgs === 0, "home carries no hero image (clean single-CTA layout)");
  ok(f.ctaBottom <= f.vh, "download CTA is above the fold at 1440x900");
  await page.close();
}

// ---- no-full-reload navigation ----
for (const [w, h] of [[1440, 900], [390, 844]]) {
  const page = await open("/", "ok", { width: w, height: h });
  const pre = `nav@${w}`;
  await page.evaluate(() => { window.__marker = 42; });
  const navLinks = await page.$$("header nav a");
  await page.click('header nav a[href="docs.html"]');
  await page.waitForFunction(() => location.pathname.endsWith("/docs.html"), { timeout: 5000 });
  await settle(page);
  let s = await page.evaluate(() => ({
    marker: window.__marker, title: document.title, focus: document.activeElement.tagName, focusText: document.activeElement.textContent.trim().slice(0, 20),
    y: scrollY, ann: document.getElementById("route-announcer").textContent,
    cur: [...document.querySelectorAll("header nav a[aria-current]")].map((a) => a.getAttribute("href")),
    sections: document.querySelectorAll("[data-doc]").length, status: document.getElementById("search-status").textContent,
  }));
  ok(s.marker === 42, pre + ": home to guide did NOT reload the document");
  ok(s.title.startsWith("מדריך למשתמש"), pre + ": title updated");
  ok(s.focus === "H1" && s.focusText.includes("מדריך"), pre + ": focus moved to the new page heading");
  await sleep(150);
  s.ann = await page.evaluate(() => document.getElementById("route-announcer").textContent);
  ok(s.ann.includes("מדריך למשתמש"), pre + ": polite announcement of the new page");
  ok(s.cur.length === 1 && s.cur[0] === "docs.html", pre + ": aria-current moved to the guide");
  ok(s.sections === 12 && s.status.includes("12"), pre + ": guide initialized (12 chapters, search status)");
  ok(s.y === 0, pre + ": scrolled to top on forward navigation");
  const cs = await ctaState(page);
  ok(!cs.disabled && cs.onclick && cs.label.includes("v0.1.0-proto.3"), pre + ": guide CTA rendered from the validated pointer after swap");
  // guide search still works after swap
  await page.type("#doc-search", "פרטיות");
  s = await page.evaluate(() => ({ vis: [...document.querySelectorAll("[data-doc]")].filter((e) => !e.hidden).length }));
  ok(s.vis >= 1 && s.vis < 12, pre + ": guide search works after swap");
  await page.evaluate(() => { const i = document.querySelector("#doc-search"); i.value = ""; i.dispatchEvent(new Event("input")); });
  // scroll then leave and return
  await page.evaluate(() => window.scrollTo(0, 1500));
  await sleep(200);
  const y1 = await page.evaluate(() => scrollY);
  await page.evaluate(() => document.querySelector('header nav a[href="index.html#download"]').click());
  await page.waitForFunction(() => location.pathname.endsWith("/") || location.pathname.endsWith("index.html"), { timeout: 5000 });
  await sleep(300);
  s = await page.evaluate(() => ({ marker: window.__marker, hash: location.hash, title: document.title, hasHero: !!document.querySelector(".hero-download-button") }));
  ok(s.marker === 42 && s.hash === "#download" && s.hasHero, pre + ": guide to home#download deeplink without reload");
  await settle(page);
  const cs2 = await ctaState(page);
  ok(!cs2.disabled && cs2.onclick, pre + ": home CTA rendered after swap (cached validated result)");
  await page.goBack();
  await page.waitForFunction(() => location.pathname.endsWith("/docs.html"), { timeout: 5000 });
  await sleep(400);
  s = await page.evaluate(() => ({ marker: window.__marker, y: scrollY, title: document.title }));
  ok(s.marker === 42 && s.title.startsWith("מדריך"), pre + ": Back returns to guide without reload, title restored");
  ok(Math.abs(s.y - y1) < 40, pre + ": Back restores scroll position (" + s.y + " vs " + y1 + ")");
  await page.goForward();
  await page.waitForFunction(() => !location.pathname.endsWith("/docs.html"), { timeout: 5000 });
  await sleep(300);
  s = await page.evaluate(() => ({ marker: window.__marker, hasHero: !!document.querySelector(".hero-download-button"), t: document.title }));
  ok(s.marker === 42 && s.hasHero && !s.t.startsWith("מדריך"), pre + ": Forward returns home without reload");
  // modifier click is NOT intercepted (new-tab semantics preserved)
  const prevented = await page.evaluate(() => {
    const a = document.querySelector('header nav a[href="docs.html"]');
    const ev = new MouseEvent("click", { bubbles: true, cancelable: true, ctrlKey: true, button: 0 });
    a.dispatchEvent(ev);
    return ev.defaultPrevented;
  });
  ok(prevented === false, pre + ": ctrl-click left to the browser (real href, new tab)");
  await page.close();
}

// ---- deep links load directly, and in-guide anchors work ----
{
  const page = await open("/docs.html#storage", "ok", { width: 1440, height: 900 });
  await sleep(2500);
  const s = await page.evaluate(() => ({ top: document.getElementById("storage").getBoundingClientRect().top, y: scrollY }));
  console.log("deeplink", JSON.stringify(s));
  ok(s.y > 500 && s.top < 400, "direct deeplink docs.html#storage lands on the chapter");
  await page.close();
}

// ---- mobile guide: sticky jump bar, back-to-top ----
{
  const page = await open("/docs.html", "ok", { width: 390, height: 844 });
  ok(await page.evaluate(() => !document.querySelector("details.verify").open && !document.querySelector("#hero-verify-body, #docs-verify-body").closest("details").open), "verification panel is collapsed by default");
  await page.evaluate(() => window.scrollTo(0, 2500));
  await sleep(300);
  const s = await page.evaluate(() => {
    const j = document.querySelector(".docs-jump");
    const r = j.getBoundingClientRect();
    return { top: r.top, h: r.height, links: [...j.querySelectorAll("a")].map((a) => a.getBoundingClientRect().height) };
  });
  ok(Math.abs(s.top) < 1 && s.links.every((h) => h >= 44), "mobile: jump bar is sticky with 44px targets");
  await page.screenshot({ path: join(SHOTS, "guide-sticky-jump-390.png") });
  await page.click('.docs-jump a[href="#main"]');
  await sleep(700);
  ok(await page.evaluate(() => scrollY) < 400, "mobile: back-to-top returns to the top of the guide");
  await page.click('.docs-jump a[href="#toc"]');
  await sleep(700);
  ok(await page.evaluate(() => document.getElementById("toc").getBoundingClientRect().top) < 120, "mobile: chapters jump reaches the contents");
  await page.close();
}

// ---- navigation regressions from independent review ----
const clickPrevented = (page, href) => page.evaluate((href) => new Promise((resolve) => {
  const a = document.createElement("a");
  a.href = href; a.textContent = "x"; document.body.appendChild(a);
  const probe = (e) => { document.removeEventListener("click", probe); e.preventDefault(); resolve({ ours: window.__ours }); };
  // ours is registered earlier on document; this later listener sees the final state.
  const late = (e) => { document.removeEventListener("click", late); resolve(e.defaultPrevented); e.preventDefault(); };
  document.addEventListener("click", late);
  a.click(); a.remove();
}), href);
{
  // hash-link entry: scroll, click #storage, Back restores the scroll
  const page = await open("/docs.html", "ok", { width: 1440, height: 900 });
  await page.evaluate(() => window.scrollTo({ top: 1500, behavior: "instant" }));
  await sleep(400);
  await page.evaluate(() => document.querySelector('.sidebar a[href="#storage"]').click());
  await sleep(1800);
  ok(await page.evaluate(() => scrollY) > 5000, "hash link moved the page to the chapter");
  await page.goBack(); await sleep(500);
  const y = await page.evaluate(() => scrollY);
  ok(Math.abs(y - 1500) < 40, "Back from a hash-link entry restores its scroll (" + y + ")");
  await page.close();
}
{
  // cross-page restore in both directions
  const page = await open("/", "ok", { width: 390, height: 400 });
  await page.evaluate(() => { window.__m = 1; });
  await page.evaluate(() => document.querySelector('header nav a[href="docs.html"]').click());
  await page.waitForFunction(() => location.pathname.endsWith("/docs.html"));
  await sleep(300);
  await page.evaluate(() => window.scrollTo({ top: 194, behavior: "instant" }));
  await sleep(400);
  await page.goBack();
  await page.waitForFunction(() => !location.pathname.endsWith("/docs.html"));
  await sleep(400);
  await page.goForward();
  await page.waitForFunction(() => location.pathname.endsWith("/docs.html"));
  await sleep(500);
  const y = await page.evaluate(() => scrollY);
  ok(Math.abs(y - 194) < 40 && await page.evaluate(() => window.__m === 1), "guide 194 -> Back -> Forward restores 194 (" + y + "), no reload");
  await page.close();
}
{
  // unrelated same-named path and same-page query are NOT intercepted
  const page = await open("/docs.html", "ok", { width: 1440, height: 900 });
  ok((await clickPrevented(page, "/other/docs.html")) === false, "/other/docs.html is left to the browser (not intercepted)");
  await page.close();
  const p2 = await open("/docs.html", "ok", { width: 1440, height: 900 });
  ok((await clickPrevented(p2, "docs.html?x=1")) === false, "same-page link with a different query is left to the browser");
  await p2.close();
}
{
  // Home clicked during a delayed guide fetch wins
  setFixture("ok");
  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900 });
  await page.setRequestInterception(true);
  page.on("request", (r) => { if (r.url().endsWith("/docs.html")) setTimeout(() => r.continue(), 1500); else r.continue(); });
  await page.goto(`${BASE}/`, { waitUntil: "domcontentloaded" });
  await settle(page);
  await page.evaluate(() => { window.__m = 5; document.querySelector('header nav a[href="docs.html"]').click(); });
  await sleep(300);
  await page.evaluate(() => document.querySelector('header .brand').click());
  await sleep(2500);
  const s = await page.evaluate(() => ({ p: location.pathname, m: window.__m, home: !!document.querySelector(".hero-download-button"), t: document.title }));
  ok(!s.p.endsWith("/docs.html") && s.home && s.m === 5 && !s.t.startsWith("מדריך"), "Home click during delayed guide fetch is not overridden by the late guide");
  await page.close();
}
{
  // canonical + description follow the swapped page
  const page = await open("/", "ok", { width: 1440, height: 900 });
  const meta = () => page.evaluate(() => ({ c: document.querySelector('link[rel="canonical"]').href, d: document.querySelector('meta[name="description"]').content }));
  const m0 = await meta();
  await page.evaluate(() => document.querySelector('header nav a[href="docs.html"]').click());
  await page.waitForFunction(() => location.pathname.endsWith("/docs.html")); await sleep(200);
  const m1 = await meta();
  ok(m1.c.endsWith("/docs.html") && m1.d.includes("מדריך"), "guide canonical + description after swap");
  await page.goBack(); await sleep(500);
  const m2 = await meta();
  ok(m2.c === m0.c && m2.d === m0.d, "home canonical + description restored on Back");
  await page.close();
}

{
  // cross-page link WITH a query is not intercepted (native navigation), exact URLs only
  const page = await open("/", "ok", { width: 1440, height: 900 });
  ok((await clickPrevented(page, "docs.html?mode=other#install")) === false, "cross-page link with a query is left to the browser");
  ok((await clickPrevented(page, "index.html?different=1")) === false, "same-page link with a different query is left to the browser");
  await page.close();
}
{
  // withdrawal: ok pointer on home, then pointer becomes 'none'; soft nav to the guide must fail closed
  const page = await open("/", "ok", { width: 1440, height: 900 });
  ok(!(await ctaState(page)).disabled, "withdrawal setup: home CTA enabled");
  setFixture("none");
  await page.evaluate(() => document.querySelector('header nav a[href="docs.html"]').click());
  await page.waitForFunction(() => location.pathname.endsWith("/docs.html"));
  await settle(page);
  let s = await ctaState(page);
  ok(s.disabled && !s.onclick && s.detailHidden && s.label === "אין הורדה זמינה כרגע", "withdrawn pointer: guide CTA fails closed after soft navigation (handler removed, panel hidden)");
  setFixture("ok");
  await page.evaluate(() => document.querySelector('header .brand').click());
  await page.waitForFunction(() => !location.pathname.endsWith("/docs.html"));
  await settle(page);
  s = await ctaState(page);
  ok(!s.disabled && s.onclick, "re-activated pointer re-enables only after a fresh validation");
  await page.close();
}

for (const [delay, root] of [[0, ""], [50, ""], [0, "/Start-app"], [50, "/Start-app"]]) {
  // hash link clicked immediately after scrolling (no timer drain): Back must restore the departing entry
  const page = await open(root + "/docs.html", "ok", { width: 1440, height: 900 });
  await page.evaluate(() => window.scrollTo({ top: 1500, behavior: "instant" }));
  if (delay) await sleep(delay);
  await page.evaluate(() => document.querySelector('.sidebar a[href="#storage"]').click());
  await sleep(1800);
  ok(await page.evaluate(() => scrollY) > 5000, `hash scroll happened (${root || "/"} delay ${delay}ms)`);
  await page.goBack(); await sleep(500);
  const y = await page.evaluate(() => scrollY);
  ok(Math.abs(y - 1500) < 40, `scroll 1500 -> ${root || "/"} ${delay}ms -> #storage -> Back restores 1500 (${y})`);
  await page.close();
}
{
  // idempotent init on an ALREADY-ENABLED DOM fails closed immediately and never nests the version
  const page = await open("/docs.html", "ok", { width: 1440, height: 900 });
  ok(!(await ctaState(page)).disabled, "idempotent-init setup: guide CTA enabled");
  const s = await page.evaluate(() => {
    window.StartAppDownload.init();
    const b = document.querySelector("[data-download-cta]");
    const det = document.getElementById(b.getAttribute("data-verify"));
    return { dis: b.disabled, onclick: typeof b.onclick === "function", hidden: det.parentNode.hidden, empty: det.textContent === "", nested: b.querySelectorAll(".ptr-ltr").length, text: b.textContent.trim() };
  });
  ok(s.dis && !s.onclick && s.hidden && s.empty && s.nested === 0 && s.text === "בודק זמינות הורדה…", "re-init on an enabled CTA: disabled, handler removed, panel hidden/empty, plain checking label (no nested version)");
  await settle(page);
  ok(!(await ctaState(page)).disabled, "re-init re-enables only after the fresh validation");
  await page.close();
  const p2 = await open("/", "ok", { width: 1440, height: 900 });
  const t = await p2.evaluate(() => { window.StartAppDownload.init(); const b = document.querySelector("[data-download-cta]"); return { dis: b.disabled, onclick: typeof b.onclick === "function", label: b.querySelector("[data-cta-label]").textContent.trim(), nested: b.querySelectorAll(".ptr-ltr").length }; });
  ok(t.dis && !t.onclick && t.label === "בודק זמינות הורדה…" && t.nested === 0, "home hero re-init: disabled, checking label, no nested version");
  await p2.close();
}

// ---- failure fallback: unreachable target falls back to a real navigation ----
{
  const page = await open("/", "ok", { width: 1440, height: 900 });
  await page.evaluate(() => { window.__marker = 7; });
  rmSync(join(SITE, "docs.html"), { force: true });
  await page.click('header nav a[href="docs.html"]');
  await sleep(1200);
  const s = await page.evaluate(() => ({ marker: window.__marker, p: location.pathname }));
  ok(s.p.endsWith("/docs.html") && s.marker !== 7, "fetch failure falls back to a real full navigation to the same URL");
  await page.close();
  cpSync(join(SITE_SRC, "docs.html"), join(SITE, "docs.html"));
}

// ---- no JavaScript: real hrefs, disabled CTAs ----
for (const path of ["/", "/docs.html"]) {
  const page = await open(path, "ok", { width: 1440, height: 900 }, false);
  const s = await page.evaluate(() => {
    const b = document.querySelector("[data-download-cta]");
    return { disabled: b.disabled, label: b.textContent.trim(), hidden: document.querySelector("details.verify").hidden,
      hrefs: [...document.querySelectorAll("header nav a")].map((a) => a.getAttribute("href")) };
  });
  ok(s.disabled && s.label === "הורדה לא זמינה" && s.hidden, `no-JS ${path}: CTA disabled with truthful label, no verification panel`);
  ok(s.hrefs.every((h) => /^(index|docs)\.html/.test(h)), `no-JS ${path}: nav links are real hrefs`);
  await page.close();
}

await browser.close();
console.log(failures === 0 ? "ALL DOM-BEHAVIOR CHECKS PASSED" : failures + " FAILURES");
process.exit(failures ? 1 : 0);
