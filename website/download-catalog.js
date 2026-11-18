"use strict";
function tr(text) { return typeof window !== "undefined" && window.StartAppI18n ? window.StartAppI18n.t(text) : text; }

/* Private catalog consumer, loaded by the home/guide pages and their localized
 * variants together with download-wire.js. This module never fetches by itself:
 * the wire fetches the same-origin downloads.json only AFTER the independently
 * validated pointer render, and passes the result here. Producer activation and
 * production catalog publication remain a separate integration.
 * Primary download selection always belongs to independently validated pointer.
 */
(function (global) {
  const P = global.StartAppPointer || (typeof require === "function" ? require("./download-pointer.js") : null);
  const MAX_BYTES = 4194304;
  const LAYOUTS = ["legacy-flat-v1", "portable-v1", "unknown"];
  const IDENTITY = ["version", "tag", "commit", "url", "label", "size", "sha256", "layout"];
  const invalid = reason => ({state: "invalid", reason});
  function pointerIdentity(result) {
    if (!result || result.state !== "ok") return null;
    return Object.assign({}, result.pointer, {layout: result.pointer.layout || "unknown"});
  }
  function validate(doc, selectedResult) {
    if (!doc || Array.isArray(doc) || doc.schema !== 1 || doc.channel !== "prototype" || doc.complete !== true || !Array.isArray(doc.entries)) return invalid("unsupported or incomplete catalog");
    if (doc.entries.length > 1000) return invalid("catalog entry limit exceeded");
    if ((doc.entries.length === 0) !== (doc.selected_tag === null)) return invalid("empty selection mismatch");
    const tags = new Set(), builds = new Set(), entries = [];
    let previous = null;
    for (const raw of doc.entries) {
      if (!raw || !LAYOUTS.includes(raw.layout)) return invalid("catalog layout required");
      if (raw.size !== null && !Number.isSafeInteger(raw.size)) return invalid("unsafe catalog size");
      const result = P.validate(raw);
      if (result.state !== "ok") return invalid("invalid catalog pointer");
      const build = BigInt(raw.version.slice(raw.version.lastIndexOf(".") + 1));
      if (tags.has(raw.tag) || builds.has(build.toString())) return invalid("duplicate tag or prototype build");
      if (previous !== null && build >= previous) return invalid("catalog not descending global prototype build");
      previous = build; tags.add(raw.tag); builds.add(build.toString());
      entries.push(result.pointer);
    }
    const primary = pointerIdentity(selectedResult);
    if (!entries.length) {
      if (!selectedResult || selectedResult.state !== "none") return invalid("empty catalog disagrees with pointer");
      return {state: "none", entries: [], selected_tag: null};
    }
    const selected = entries.find(entry => entry.tag === doc.selected_tag);
    if (!selected || !primary || !IDENTITY.every(key => selected[key] === primary[key])) return invalid("selected catalog identity disagrees with pointer");
    // Existing producer chooses greatest global prototype build, not SemVer.
    if (entries[0].tag !== doc.selected_tag) return invalid("selected tag is not highest global prototype build");
    return {state: "ok", entries, selected_tag: doc.selected_tag};
  }
  async function load(url, selectedResult) {
    try {
      const parsed = new URL(url, global.location && global.location.href);
      if (parsed.protocol !== "https:" || !global.location || parsed.origin !== global.location.origin) return invalid("catalog URL is not same-origin HTTPS");
      const response = await fetch(parsed.href, {cache: "no-store", credentials: "omit", redirect: "error"});
      if (!response.ok) return invalid("catalog fetch failed");
      const reader = response.body && response.body.getReader();
      let text = "";
      if (reader) {
        const decoder = new TextDecoder(); let bytes = 0;
        for (;;) {
          const chunk = await reader.read();
          if (chunk.done) break;
          bytes += chunk.value.byteLength;
          if (bytes > MAX_BYTES) { await reader.cancel(); return invalid("catalog byte limit exceeded"); }
          text += decoder.decode(chunk.value, {stream: true});
        }
        text += decoder.decode();
      } else {
        const bytes = await response.arrayBuffer();
        if (bytes.byteLength > MAX_BYTES) return invalid("catalog byte limit exceeded");
        text = new TextDecoder().decode(bytes);
      }
      return validate(JSON.parse(text), selectedResult);
    } catch (_) { return invalid("catalog unavailable or invalid JSON"); }
  }
  function render(target, result) {
    // Clear even detached handlers: callers holding an old row cannot click it.
    target.querySelectorAll("button").forEach(button => {button.disabled = true; button.onclick = null;});
    target.replaceChildren();
    if (result.state !== "ok") {
      const note = document.createElement("p"); note.className = "inventory-note";
      note.textContent = typeof global.StartAppI18n !== "undefined" ? global.StartAppI18n.t(result.state === "none" ? "אין הורדה זמינה כרגע" : "הורדה לא זמינה") : result.state === "none" ? "אין הורדה זמינה כרגע" : "הורדה לא זמינה";
      target.appendChild(note); return;
    }
    result.entries.forEach(pointer => {
      const row = document.createElement("div"); row.className = "prototype-offer";
      const version = document.createElement("bdi"); version.textContent = pointer.version;
      const button = document.createElement("button"); button.type = "button"; button.disabled = true;
      P.render({button}, {state: "ok", pointer});
      // Repeated rows share one visible label: the accessible name mirrors the
      // full localized visible free-download wording, then the exact version and
      // tag (LTR-isolated), so every history control is distinct and complete.
      button.setAttribute("aria-label", tr("הורדה חינם") + " \u2066" + pointer.version + "\u2069 (\u2066" + pointer.tag + "\u2069)");
      row.appendChild(version); row.appendChild(button); target.appendChild(row);
    });
  }
  const api = {validate, load, render};
  global.StartAppCatalog = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
