# Local multilingual review draft

This is a local design/development candidate, not a published release.

- Hebrew stays the source language. Added languages: English, Russian, Simplified Chinese, Japanese, French, Spanish and German.
- Simplified Chinese is a provisional preview variant. It is not a final choice between Simplified and Traditional Chinese.
- The original translations are Google Translate drafts of public website copy with limited editorial terminology corrections. They have not had native-speaker review. This is not a linguistic or accessibility certification.
- There are 193 source entries per added language, including headings, guide text, alt text, accessible labels, descriptions and dynamic download/search messages.
- Start-app stays literal. Platform names, file paths, code examples, release validation and package identity are not translated.
- Existing app screenshots stay in Hebrew. Only the website is localized; the application/package is not changed.
- Hebrew is RTL; other pages are LTR. Release metadata stays isolated LTR. Asian languages use locally available system fallback fonts, with no remote font calls. Actual Windows/Android fallback-font rendering still needs device review.
- Language switching uses real links and works without JavaScript. Each localized page ships with a disabled download button and localized explanation. JavaScript validates the shared same-origin pointer before enabling any download. There is no static release download bypass.
- Runtime uses only local files. No translation-service call, analytics, account, payment or remote font dependency is added.
- A separate LOCAL metadata-preparation delta now gives each of the16pages its own canonical, reciprocal8-language plus x-default alternates, and matching16-page sitemap entries. This is proposed publication metadata, not live/search-engine verification or authorization to publish. Simplified Chinese paths remain provisional and must be revised consistently if Traditional Chinese is chosen. Translation sign-off still precedes any publication.

## Source files

`source-index.html` and `source-docs.html` capture the typography/icon baseline used for the translations. `catalogs.json` is the editable source-to-translation review catalog. `../i18n.js` bundles the same catalog for dynamic messages; the language HTML pages already contain their translated static content. If translations change, update both the relevant static HTML and the runtime catalog where the string is dynamic, then rerun the checks below.

## Local tests

From the repository root:

```
node website/tests/test-download-pointer.mjs
node website/tests/test-search-visibility.mjs
node website/tests/test-i18n.mjs
cd website/tests/dom-harness
npm install --ignore-scripts
npm test
ONLY_LOCALE=he,en,ru,zh-Hans node test-locales.mjs
ONLY_LOCALE=ja,fr,es,de node test-locales.mjs
```

The locale test starts a temporary localhost HTTPS server using the repository's dummy pointer fixtures. It blocks all requests outside that localhost origin. It checks normal and fail-closed states, layouts, search, navigation, no-JS behavior and the language menu, and writes clearly labeled dummy-data screenshots. It does not fetch the release ZIP. Chrome and OpenSSL are required, as in the existing DOM harness.

The normal `npm test` harness includes a download-navigation check; for this local review it was run through a temporary external-request-blocking wrapper to prevent fetching release bytes. The shipped locale test always blocks external requests itself.

## Design choices

Cobalt/ice is the selected palette direction for the next LOCAL preview. Typography is bundled Heebo with an Arial bold wordmark and an initial S scaled horizontally to 1.4. The corrected header keeps the website icon to the right of the intact LTR wordmark. New product-specific icon concepts are in `../icons/`: S route, S workspace and S link. They are locally drawn review concepts, not proven globally unique marks. The previous generic arrow/window/stack choice sheet is superseded. The existing repository arrow remains the provisional draft header, not a final icon selection. No app/package icon change is included.

## Icon review status

The three S-route/S-workspace/S-link sketches were rejected by the requester. They are historical review assets only, not approved icons. An exploratory inverted-UFO/collecting-beam sheet was delivered separately; no icon selected. Existing header arrow is temporary.

## Focused independent-review corrections

The review of local tree e3c999 identified three shared Hebrew translation traps: last-open timestamp (not unlocking), the Launch button (not routine), and an optional description (not permission). The seven added languages now have corrected paragraphs and matching main-screen image alt text in the catalog, bundled runtime and static guide. `tests/meaning-corrections.json` records the exact focused corrections; `node website/tests/test-meaning-corrections.mjs` checks consistency across all three surfaces. This targeted correction is not native-language review or certification of the remaining copy. Simplified Chinese remains provisional.

SPA navigation now replaces the destination's complete hreflang set, including x-default. The offline DOM test compares all nine links against direct-page metadata after home-to-guide, back, forward and guide-to-home navigation in each locale. Source HTML/sitemap checks alone do not cover this behavior. No live search-engine indexing claim follows from these tests.

## Versioned-guide review candidate

Default guides document proto2 in all eight languages. The proto1 archive preserves the current live guide content under `proto1/`, with adjusted asset/navigation paths, an added version selector, and fixed-version download-list links in place of dynamic guide download controls. New proto2 translations are editorial drafts, not native-speaker certification. Original application screenshots are reused unchanged; only captions and alt text are translated. Reopen choices and opening results are text only.

Guide links select the same-language versions list using `?version=v0.1.0-proto.N#download`. Only an exact validated catalog row is highlighted; missing catalog/version shows a truthful note, with no latest-version substitution. The existing home-page primary button is unchanged. Proto1 is not added to the existing 16-entry sitemap. No release/pin/workflow/publication change is included.

`node website/tests/dom-harness/test-versioned-guides.mjs` checks all 16 combinations through a local HTTPS fixture server, including desktop/mobile, version/language navigation, back/forward, resources, highlight, missing version/catalog and blocked external requests. Screenshots are local evidence, not real release availability.
