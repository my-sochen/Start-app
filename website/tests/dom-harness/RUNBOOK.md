# DOM harness - website download-pointer states

Drives the real website in real Chrome over local HTTPS and asserts behavior and
computed styles for every download-pointer state, including the no-JavaScript
shipped state. Produces screenshots for pixel review.

## Prerequisites

- Node.js 20+ (developed on v22)
- Google Chrome or Chromium on the machine (default path `/usr/bin/google-chrome`,
  override with `HARNESS_CHROME`)
- `openssl` (used once to mint a throwaway self-signed cert for `127.0.0.1`)

## Run

```sh
cd website/tests/dom-harness
npm install        # installs puppeteer-core only; no browser download
node drive.mjs     # or: npm test
```

`drive.mjs` does everything itself:

1. mints `certs/cert.pem`+`key.pem` if absent (openssl, 2-day self-signed);
2. copies the website files from `../../` plus `fixtures/` into a temp dir
   (the repo tree stays clean) and starts `server.js` on it;
3. drives headless Chrome through the states `ok`, `none`, `invalid`, `missing`
   (fixture applied by copying `fixtures/<state>.json` to `pointer.json`),
   asserting rendered copy, button URLs, computed styles (enabled green +
   `cursor:pointer`, disabled gray + `cursor:not-allowed`), no horizontal
   overflow, and unbroken version tokens at 1440, 390 and 320 px;
4. runs a final pass with JavaScript disabled and asserts the shipped static
   state is the truthful "cannot verify - download stays off" copy with the
   `<noscript>` explanation, never a stuck "checking" state;
5. writes screenshots to `screenshots/` and prints PASS/FAIL lines; exit code
   is non-zero on any failure.

## Environment overrides

| Variable             | Default                          | Meaning                          |
| -------------------- | -------------------------------- | -------------------------------- |
| `HARNESS_CHROME`     | `/usr/bin/google-chrome`         | Chrome/Chromium executable       |
| `HARNESS_PORT`       | `8443`                           | local HTTPS port                 |
| `HARNESS_SCREENSHOTS`| `./screenshots`                  | screenshot output directory      |

## Notes

- The harness expects the fixture `ok.json` release URL
  (`.../releases/download/v0.1.0-proto.3/StartApp-win-x64.zip`); when the real
  release moves, update the fixture and `EXPECTED_URL` together.
- `certs/` and `screenshots/` are generated artifacts; do not commit them.
