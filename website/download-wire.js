"use strict";
function tr(text) { return typeof window !== "undefined" && window.StartAppI18n ? window.StartAppI18n.t(text) : text; }

/*
 * Activation wiring for the prototype download pointer.
 * Loads the same-origin pointer document (pointer.json, deployed atomically
 * with this site) on every page view and renders EVERY download CTA on the
 * current page through StartAppPointer.render: any element marked
 * data-download-cta (home hero, guide install chapter). One code path means every CTA fails closed identically: a button
 * is clickable only after a fully valid pointer, and never has a fallback URL.
 *
 * CTA markup contract: <button data-download-cta aria-describedby="NOTE_ID"
 * data-verify="DETAIL_ID"> with an optional inner <span data-cta-label> as the label.
 * DETAIL_ID is the body of a collapsed verification panel (its parent is
 * hidden unless the state is ok).
 *
 * init() is idempotent and is called again by the client-side router after a
 * page swap; each call revalidates the pointer (no cache), so a CTA is only
 * ever enabled or labeled from a freshly validated pointer result.
 *
 * Settlement signal: when the router passes its navigation token as
 * init(navToken), the terminal render of this init (success, fail-closed, and
 * every catalog branch) dispatches "startapp:download-settled" on the document
 * with detail.token set to it. A superseded init (a newer init started first)
 * never dispatches: the newest navigation's own init settles it. The router
 * uses the event to end its scroll-restore defense at the real render
 * settlement instead of a timer or a permanent correction.
 */
(function () {
  var TEXTS = {
    ok: {
      badge: tr("גרסת אב-טיפוס זמינה להורדה"),
      heading: tr("גרסת אב-טיפוס זמינה."),
    },
    none: {
      badge: tr("אין עדיין גרסה מאומתת להורדה"),
      heading: tr("אין הורדה זמינה כרגע."),
    },
    invalid: {
      badge: tr("לא ניתן לאמת הורדה כרגע"),
      heading: tr("לא ניתן לאמת הורדה כרגע."),
    },
  };
  var seq = 0;

  function byId(id) {
    return id ? document.getElementById(id) : null;
  }

  function labelOf(button) {
    return button.querySelector("[data-cta-label]");
  }

  function ctas() {
    return Array.prototype.slice.call(
      document.querySelectorAll("[data-download-cta]")
    );
  }

  function applyStaticState(state) {
    var texts = TEXTS[state] || TEXTS.invalid;
    var badge = document.getElementById("availability-badge");
    var heading = document.getElementById("download-heading");
    if (badge) badge.textContent = texts.badge;
    if (heading) heading.textContent = texts.heading;
  }

  // With JavaScript running we CAN check: upgrade the static no-JS state to
  // the truthful "checking" state before the fetch resolves it.
  function applyLoading() {
    var badge = document.getElementById("availability-badge");
    if (badge) badge.textContent = tr("בודק זמינות הורדה…");
    document.querySelectorAll("[data-cta-version]").forEach(function (node) { node.textContent = ""; node.hidden = true; });
    renderOffers(null, true);
    ctas().forEach(function (button) {
      // Fail closed first: even on an already-enabled CTA (init called again),
      // the button is disabled, the old handler removed and the verification
      // panel hidden until a fresh pointer validates.
      button.disabled = true;
      button.onclick = null;
      var detail = byId(button.getAttribute("data-verify"));
      if (detail && detail.parentNode) {
        detail.textContent = "";
        detail.parentNode.hidden = true;
      }
      var label = labelOf(button);
      var note = byId(button.getAttribute("aria-describedby"));
      if (label) label.textContent = tr("בודק זמינות הורדה…");
      else button.textContent = tr("בודק זמינות הורדה…");
      if (note) note.textContent = tr("יעד האריזה: Windows x64. בודק אם קיימת גרסת אב-טיפוס מאומתת…");
    });
  }

  // Truthful loading state: while a check is in progress the offers region says
  // so; it never claims "unavailable" before any answer arrived.
  function renderOffers(result, loading) {
    document.querySelectorAll('[data-prototype-offers]').forEach(function (target) {
      target.querySelectorAll('button').forEach(function (button) {
        button.onclick = null;
        button.disabled = true;
      });
      target.replaceChildren();
      if (loading || result.state !== 'ok') {
        var empty = document.createElement('p');
        empty.className = 'inventory-note';
        empty.textContent = tr(loading ? 'בודק זמינות הורדה…' : result.state === 'none' ? 'אין הורדה זמינה כרגע' : 'הורדה לא זמינה');
        target.appendChild(empty);
        return;
      }
      var row = document.createElement('div');
      row.className = 'prototype-offer';
      var version = document.createElement('bdi');
      version.textContent = result.pointer.version;
      var button = document.createElement('button');
      button.type = 'button';
      button.disabled = true;
      window.StartAppPointer.render({button: button}, result);
      // Accessible name mirrors the full localized visible free-download
      // wording, then the exact version and tag (LTR-isolated).
      button.setAttribute('aria-label', tr('הורדה חינם') + ' \u2066' + result.pointer.version + '\u2069 (\u2066' + result.pointer.tag + '\u2069)');
      row.appendChild(version);
      row.appendChild(button);
      target.appendChild(row);
    });
  }

  function renderAll(result) {
    applyStaticState(result.state);
    renderOffers(result);
    document.querySelectorAll("[data-cta-version]").forEach(function (node) { node.textContent = result.state === "ok" ? result.pointer.version : ""; node.hidden = result.state !== "ok"; });
    ctas().forEach(function (button) {
      window.StartAppPointer.render(
        {
          button: button,
          note: byId(button.getAttribute("aria-describedby")),
          detail: byId(button.getAttribute("data-verify")),
          label: labelOf(button),
        },
        result
      );
    });
  }

  function guideLayout(result) {
    var layout = result.state === "ok" ? result.pointer.layout : "unknown";
    document.querySelectorAll("[data-layout-legacy]").forEach(function (node) { node.hidden = layout !== "legacy-flat-v1"; });
    // Historical runtime claim belongs only to the exact documented package,
    // never to every package which happens to share its folder layout.
    // Schema/channel/asset and exact tag URL already pass the fixed validator.
    var documented = {"version": "0.1.0-proto.1", "tag": "v0.1.0-proto.1", "commit": "db94adc19056b6dd68d338c030c6ce4983a3057c", "label": "אב-טיפוס להערכה - לא גרסה יציבה", "size": 65356245, "sha256": "7e8ee4d53bcba3626543aebae97c08c5bfc41057582af1e03f3762f68ec6c702"};
    var qualified = result.state === "ok" && Object.keys(documented).every(function (key) { return result.pointer[key] === documented[key]; });
    document.querySelectorAll("[data-layout-legacy-qualification]").forEach(function (node) { node.hidden = !qualified; });
    // Package-specific claims (self-contained runtime, no sign-in, exact local
    // data path) are identity-gated the same way: without the exact documented
    // package the guide shows only clearly current-project-design wording.
    document.querySelectorAll("[data-layout-legacy-unqualified]").forEach(function (node) { node.hidden = qualified; });
    document.querySelectorAll("[data-layout-portable]").forEach(function (node) { node.hidden = layout !== "portable-v1"; });
    document.querySelectorAll("[data-layout-unknown]").forEach(function (node) { node.hidden = layout === "legacy-flat-v1" || layout === "portable-v1"; });
  }

  function historyHint(complete) {
    document.querySelectorAll(".download-all-hint").forEach(function (node) {
      node.textContent = tr(complete ? "ארכיון אב-טיפוס מאומת · תמונת מצב בעת הפרסום" : "היצע מאומת נוכחי · לא ארכיון גרסאות מלא");
    });
  }

  // Static review flagged a pointer-init render error leaving the page mid-state
  // as an unreproduced potential risk. It stays unreproduced; this guard makes
  // the outcome deterministic anyway: any render failure fails closed, every
  // control disabled with the honest cannot-verify state, no stale handler.
  function failClosed() {
    try { applyStaticState("invalid"); } catch (_) {}
    try { guideLayout({state: "invalid"}); } catch (_) {}
    try { historyHint(false); } catch (_) {}
    try { renderOffers({state: "invalid"}); } catch (_) {}
    ctas().forEach(function (button) {
      try {
        button.disabled = true;
        button.onclick = null;
        var label = labelOf(button);
        if (label) label.textContent = tr("הורדה לא זמינה"); else button.textContent = tr("הורדה לא זמינה");
        var note = byId(button.getAttribute("aria-describedby"));
        if (note) note.textContent = tr("לא ניתן לאמת כרגע גרסה להורדה. ההורדה נשארת כבויה.");
        var detail = byId(button.getAttribute("data-verify"));
        if (detail && detail.parentNode) { detail.textContent = ""; detail.parentNode.hidden = true; }
      } catch (_) {}
    });
  }

  function init(navToken) {
    var mine = ++seq;
    var settled = false;
    // Exactly one settlement signal per init that reaches its terminal render.
    function settle() {
      if (settled) return;
      settled = true;
      document.dispatchEvent(
        new CustomEvent("startapp:download-settled", {
          detail: { token: navToken === undefined ? null : navToken },
        })
      );
    }
    if (!window.StartAppPointer) {
      applyLoading(); applyStaticState("invalid"); guideLayout({state: "invalid"});
      settle();
      return;
    }
    applyLoading();
    guideLayout({state: "invalid"});
    historyHint(false);
    var root = (document.documentElement.getAttribute("data-site-root") || ".") + "/";
    var url = new URL(root + "pointer.json", window.location.href).href;
    window.StartAppPointer.load(url).then(function (result) {
      if (mine !== seq) return;
      // Independently valid primary/guide renders without waiting on history.
      try {
        renderAll(result);
        guideLayout(result);
      } catch (_) { failClosed(); settle(); return; }
      if (!window.StartAppCatalog || !document.querySelector("[data-prototype-offers]")) { settle(); return; }
      var catalogUrl = new URL(root + "downloads.json", window.location.href).href;
      window.StartAppCatalog.load(catalogUrl, result).then(function (history) {
        if (mine !== seq) return;
        try {
          // Old schema1 pointer without layout remains a usable singleton when
          // catalog absent/conflicting. Never relabel that fallback complete.
          if (history.state === "invalid" && result.state === "ok" && result.pointer.layout === "unknown") {
            renderOffers(result); historyHint(false); settle(); return;
          }
          document.querySelectorAll("[data-prototype-offers]").forEach(function (target) { window.StartAppCatalog.render(target, history); });
          historyHint(history.state === "ok" || history.state === "none");
        } catch (_) {
          // A history render failure only clears history rows; the independently
          // validated primary and guide keep their own verified state.
          try { renderOffers({state: "invalid"}); historyHint(false); } catch (_) {}
        }
        settle();
      });
    });
  }

  window.StartAppDownload = { init: init };
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
