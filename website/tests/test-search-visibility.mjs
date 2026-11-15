/* Offline static checks for the search-visibility preparation slice.
 * No network, no deps: node website/tests/test-search-visibility.mjs */
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const SITE = join(dirname(fileURLToPath(import.meta.url)), "..");
const ORIGIN = "https://my-sochen.github.io/Start-app";
let passed = 0;
function t(name, fn) {
  fn();
  passed++;
  console.log("PASS", name);
}

const INDEX = readFileSync(join(SITE, "index.html"), "utf8");
const DOCS = readFileSync(join(SITE, "docs.html"), "utf8");
const ROBOTS = readFileSync(join(SITE, "robots.txt"), "utf8");
const SITEMAP = readFileSync(join(SITE, "sitemap.xml"), "utf8");

t("no noindex/nofollow remains on either page (owner-approved indexability)", () => {
  assert.ok(!/noindex|nofollow/i.test(INDEX), "index.html indexable");
  assert.ok(!/noindex|nofollow/i.test(DOCS), "docs.html indexable");
});

t("each page carries its own absolute canonical on the approved origin", () => {
  assert.ok(INDEX.includes(`<link rel="canonical" href="${ORIGIN}/" />`));
  assert.ok(DOCS.includes(`<link rel="canonical" href="${ORIGIN}/docs.html" />`));
});

t("robots.txt allows crawling and points at the absolute sitemap", () => {
  assert.ok(/User-agent: \*/.test(ROBOTS));
  assert.ok(/Allow: \//.test(ROBOTS));
  assert.ok(!/Disallow: \//.test(ROBOTS), "nothing disallowed");
  assert.ok(ROBOTS.includes(`Sitemap: ${ORIGIN}/sitemap.xml`));
});

t("sitemap lists exactly the canonical pages on the approved origin", () => {
  const locs = [...SITEMAP.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);
  assert.deepEqual(locs.sort(), [`${ORIGIN}/`, `${ORIGIN}/docs.html`].sort());
  for (const loc of locs) assert.ok(loc.startsWith("https://"), "absolute https");
});

t("sitemap URLs match the pages' canonical URLs", () => {
  for (const [html, file] of [[INDEX, "index.html"], [DOCS, "docs.html"]]) {
    const canon = html.match(/rel="canonical" href="([^"]+)"/)[1];
    assert.ok(SITEMAP.includes(`<loc>${canon}</loc>`), file + " canonical in sitemap");
  }
});

console.log(passed + " tests passed");
