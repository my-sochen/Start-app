# Search visibility preparation (Google) - Start-app site

Owner request (2026-11-15, WhatsApp): the site should appear in Google search
results. Scope here is PREPARATION only: what the site itself can carry, plus
the exact owner-only steps that remain. Nothing is published; indexing and
ranking are Google's decisions and are not guaranteed or timed by us.

## What changed in code (this slice, website lane - proposal for Moshe)

1. Removed `<meta name="robots" content="noindex,nofollow">` from index.html
   and docs.html. This is THE deliberate semantics change: the pages were
   marked non-indexable for the private review stage, and the owner's request
   makes indexability a launch requirement. Without this removal nothing else
   here can work. The strip copy was reworded in the activation draft (v5, Moshe's review):
   it no longer claims a private review stage, so it stays truthful when the
   site is public.
2. Added an absolute `<link rel="canonical">` per page pointing at the
   approved hosting address:
   - https://my-sochen.github.io/Start-app/
   - https://my-sochen.github.io/Start-app/docs.html
3. Added `website/robots.txt`: allows all crawlers, points at the sitemap.
4. Added `website/sitemap.xml`: the two pages above, lastmod 2026-11-15.
   update_pointer.py already deploys the FULL website directory atomically,
   so robots.txt and sitemap.xml ride along with no pipeline change.

## What remains (owner-only steps, after publication is separately approved)

1. Publish the site (separate owner approval, out of scope here).
2. Verify ownership in Google Search Console (needs the owner's Google
   account): URL-prefix property for https://my-sochen.github.io/Start-app/,
   HTML-tag method; the one-line verification meta gets added to index.html
   at that time (the token comes from Search Console).
   https://support.google.com/webmasters/answer/9008080
3. In Search Console: submit the sitemap URL
   (https://my-sochen.github.io/Start-app/sitemap.xml) and use URL Inspection
   -> "Request indexing" on the two pages.
4. Discovery check afterwards: `site:my-sochen.github.io/Start-app` query.
   Google's own guidance: new sites can take weeks to be crawled; a sitemap
   helps discovery but does not guarantee crawling or indexing.

## Grounding (read 2026-11-15)

- Get on Google: https://developers.google.com/search/docs/fundamentals/get-on-google
  (posting the site is usually enough; missed-site causes; site: query;
  new sites can take weeks; Search Console submission path)
- robots meta tag: https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag
  (noindex blocks appearance in results - why its removal is required)
- Sitemaps overview: https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview
  (sitemap aids discovery, does not guarantee indexing; small well-linked
  sites may not need one - kept anyway, it costs nothing)
- Canonical: https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls
- Search Console verification: https://support.google.com/webmasters/answer/9008080

## Honest limits

- "Appears in Google" for the brand term (Start-app / Startapp) is a
  hypothesis, not settled: a github.io project page with little external
  linkage may rank weakly regardless of this preparation. No ranking
  promise is made or implied.
- This slice changes no hosting/settings state and publishes nothing.
