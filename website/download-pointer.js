"use strict";
/*
 * Start-app prototype download pointer reader (WIRED by download-wire.js).
 *
 * This module is the website side of .github/release/pick_release_pointer.py.
 * It validates a pointer document (schema 1) emitted by that selector and
 * renders the download state for both CTAs.
 *
 * Contract:
 *  - The module makes NO network call by itself. download-wire.js passes the
 *    same-origin pointer.json URL (deployed atomically with the site).
 *  - The page CSP is connect-src 'self': the same-origin fetch is allowed and
 *    no external origin is needed.
 *  - validate() never invents a URL: every field must come from the document,
 *    and the download URL must come from the published release (the selector
 *    already enforces that).
 *  - The disabled, honest default state is preserved for every outcome that is
 *    not a fully valid pointer (no candidate, invalid document, fetch failure).
 *
 * Integration contract (for the activation change and for any CTA redesign):
 *   StartAppPointer.validate(doc) -> {state:"ok", pointer} | {state:"none", reason}
 *                                    | {state:"invalid", reason}
 *   StartAppPointer.render(els, result) - els: {button, note} (button is a
 *     <button>; render only rewires its click handler and text, so the CTA
 *     markup/styling stays owned by the page design).
 *   StartAppPointer.load(url) -> Promise<result> (fetch + validate; https only).
 */
(function (global) {
  const VERSION_RE = /^[0-9]+\.[0-9]+\.[0-9]+-proto\.[0-9]+$/;
  const SHA_RE = /^[0-9a-f]{40}$/;
  const SHA256_RE = /^[0-9a-f]{64}$/;
  const ASSET = "StartApp-win-x64.zip";
  // The only origin a download URL may point at: this repo's Releases. A pointer
  // naming any other host, repo path, tag or filename fails validation - the
  // site never sends the user somewhere the pipeline did not publish.
  const REPO = "my-sochen/Start-app";

  function invalid(reason) {
    return { state: "invalid", reason: reason };
  }

  function validate(doc) {
    if (!doc || typeof doc !== "object" || Array.isArray(doc)) {
      return invalid("pointer document is not a JSON object");
    }
    if (doc.schema !== 1) return invalid("unsupported pointer schema");
    if (doc.channel !== "prototype") return invalid("pointer channel is not prototype");
    // The selector emits {candidate: null, reason} when nothing qualifies:
    // "no published prerelease with ui_gate=passed exists" is a normal result.
    if (doc.candidate === null) {
      return {
        state: "none",
        reason: typeof doc.reason === "string" ? doc.reason : "no qualifying candidate",
      };
    }
    if (typeof doc.version !== "string" || !VERSION_RE.test(doc.version)) {
      return invalid("version is not X.Y.Z-proto.N");
    }
    if (doc.tag !== "v" + doc.version) return invalid("tag does not match version");
    if (typeof doc.commit !== "string" || !SHA_RE.test(doc.commit)) {
      return invalid("commit is not a full 40-char sha");
    }
    if (doc.asset !== ASSET) return invalid("asset is not " + ASSET);
    if (typeof doc.url !== "string" || !doc.url.startsWith("https://")) {
      return invalid("download URL missing or not https");
    }
    const expected =
      "https://github.com/" + REPO + "/releases/download/" + doc.tag + "/" + ASSET;
    if (doc.url !== expected) {
      return invalid("download URL is not this repo's release asset for the tag");
    }
    if (typeof doc.label !== "string" || doc.label.length === 0) {
      return invalid("prototype label missing");
    }
    if (doc.sha256 !== null && (typeof doc.sha256 !== "string" || !SHA256_RE.test(doc.sha256))) {
      return invalid("sha256 is not 64 lowercase hex");
    }
    if (doc.size !== null && (!Number.isInteger(doc.size) || doc.size <= 0)) {
      return invalid("size is not a positive integer");
    }
    return {
      state: "ok",
      pointer: {
        version: doc.version,
        tag: doc.tag,
        commit: doc.commit,
        url: doc.url,
        label: doc.label,
        sha256: doc.sha256,
        size: doc.size,
      },
    };
  }

  function formatSize(bytes) {
    if (bytes === null) return null;
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
  }

  // render only mutates the elements it is given. On any non-ok result it keeps
  // the button disabled and writes an honest note; it never renders a URL that
  // did not pass validate(). els.label is optional: a rich CTA (icons, nested
  // markup) passes the specific element whose text may change, so the CTA's own
  // markup is never flattened; without it the button's textContent is set.
  function render(els, result) {
    const button = els.button;
    const note = els.note;
    if (!button) return;
    const setLabel = function (text) {
      if (els.label) els.label.textContent = text;
      else button.textContent = text;
    };
    if (result.state === "ok") {
      const p = result.pointer;
      button.disabled = false;
      setLabel("הורדה ל־Windows - " + p.tag);
      // onclick assignment, not addEventListener: repeated renders replace the
      // binding instead of accumulating handlers, and a later non-ok render can
      // clear it (no stale URL stays clickable).
      button.onclick = function () {
        global.location.assign(p.url);
      };
      if (note) {
        const parts = [p.label + " · גרסה " + p.version];
        const size = formatSize(p.size);
        if (size) parts.push(size);
        if (p.sha256) parts.push("sha256: " + p.sha256);
        parts.push("הקישור נקבע ממטמון הגרסה שפרסם צינור הבדיקות; הבייטים עצמם לא נבדקו מחדש בדף זה.");
        note.textContent = parts.join(" · ");
      }
      return;
    }
    button.disabled = true;
    button.onclick = null;
    setLabel(
      result.state === "none" ? "אין הורדה זמינה כרגע" : "הורדה לא זמינה"
    );
    if (note) {
      note.textContent =
        result.state === "none"
          ? "אין עדיין גרסת אב-טיפוס שעברה את כל הבדיקות ואושרה להורדה."
          : "לא ניתן לאמת כרגע גרסה להורדה. ההורדה נשארת כבויה.";
    }
  }

  function load(pointerUrl) {
    if (typeof pointerUrl !== "string" || !pointerUrl.startsWith("https://")) {
      return Promise.resolve(invalid("pointer URL is not https"));
    }
    return fetch(pointerUrl, { cache: "no-store", credentials: "omit" })
      .then(function (res) {
        if (!res.ok) return invalid("pointer fetch failed: HTTP " + res.status);
        return res.json().then(validate, function () {
          return invalid("pointer response is not JSON");
        });
      })
      .catch(function () {
        return invalid("pointer fetch failed");
      });
  }

  global.StartAppPointer = { validate: validate, render: render, load: load };
  if (typeof module !== "undefined" && module.exports) {
    module.exports = global.StartAppPointer;
  }
})(typeof window !== "undefined" ? window : globalThis);
