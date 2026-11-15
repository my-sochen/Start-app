# Start-app website

Static site: a minimal landing page (`index.html`) and a user guide (`docs.html`, twelve searchable chapters with real application screenshots in `shots/`). No build step, package install, external fonts or runtime dependencies.

## Preview

Open `index.html` directly in a browser, or serve this directory locally:

```sh
python3 -m http.server 8080 --bind 127.0.0.1
```

Then visit `http://127.0.0.1:8080/`. The guide search runs locally; all text is readable without JavaScript. Browser printing restores every chapter even if the search is filtering the screen.

The download buttons are disabled by design. Internal project material (sources, status, developer notes, check suite) lives outside this folder.
