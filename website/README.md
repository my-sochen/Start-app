# Start-app website review bundle

Private review code. No live site, active download, deployment, remote branch or merge.

## Open

Open `index.html` in a browser, or from this directory run:

```sh
python3 -m http.server 8080 --bind 127.0.0.1
```

Then open `http://127.0.0.1:8080/`. The site has no build step or runtime dependencies. `docs.html` includes all system documentation, with local progressive search. All text remains readable with JavaScript disabled. Print the documentation from the browser, or use the attached review PDF.

## Contents

- `index.html`: Hebrew RTL product landing page; download intentionally disabled.
- `docs.html`: eleven system documentation chapters.
- `styles.css`: responsive layout, keyboard focus, print and reduced-motion support.
- `docs.js`: local-only documentation search, no network calls or persistence.
- `SOURCE-BASELINE.json`: exact inspected application refs and selected integration destination.

## Integration boundary

Project management selected `website/` in the existing `my-sochen/Start-app` repository, a dedicated `feat/website-docs-review` branch based on `dev`. Future review target is `dev`; no PR is requested now. This bundle prepares the selected layout and local branch only, not a public remote push. No nested `.git`. Re-fetch current refs and compare any concurrent website work before copying these files. No workflow, DNS, account, infrastructure or stable-pointer changes are included.

The download has no target and cannot be enabled by a remote configuration value. Connecting it is a separate reviewed code change after the current artifact is validated and the requester approves delivery. No archived executable is included or linked. Private review delivery is not permission to publish this site or this source branch to a public audience.

## Evidence boundary

Application details are source-level findings from PR19 head 9e681f1, a draft not merged into dev. They must not be mistaken for features proved in the dev base or for new WPF runtime verification. The old archived 163f439 artifact is not the selected upcoming version. Extension and session-file experiments are attributed limited project reports, not new tests. No full browser compatibility, final privacy policy, stable release or completed CD is claimed. Source snapshot and remaining product decisions are documented within the site.
