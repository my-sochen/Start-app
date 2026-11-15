"use strict";
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
 */
(function () {
  var TEXTS = {
    ok: {
      badge: "גרסת אב-טיפוס זמינה להורדה",
      heading: "גרסת אב-טיפוס זמינה.",
    },
    none: {
      badge: "אין עדיין גרסה מאומתת להורדה",
      heading: "אין הורדה זמינה כרגע.",
    },
    invalid: {
      badge: "לא ניתן לאמת הורדה כרגע",
      heading: "לא ניתן לאמת הורדה כרגע.",
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
    if (badge) badge.textContent = "בודק זמינות הורדה…";
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
      if (label) label.textContent = "בודק זמינות הורדה…";
      else button.textContent = "בודק זמינות הורדה…";
      if (note) note.textContent = "יעד האריזה: Windows x64. בודק אם קיימת גרסת אב-טיפוס מאומתת…";
    });
  }

  function renderAll(result) {
    applyStaticState(result.state);
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

  function init() {
    if (!window.StartAppPointer) {
      applyStaticState("invalid");
      return;
    }
    // Every call (first load AND every client-side page swap) re-fetches the
    // pointer with no-store and shows the disabled "checking" state until it
    // validates, so a withdrawn or changed release closes the gate on the next
    // page view, exactly like a full reload. Only the newest request may
    // render; an older, slower response never overwrites a newer one.
    var mine = ++seq;
    applyLoading();
    // Same-origin resolution: pointer.json sits next to this page in the
    // deployed artifact; an absolute https URL satisfies load()'s own check.
    var url = new URL("pointer.json", window.location.href).href;
    window.StartAppPointer.load(url).then(function (result) {
      if (mine !== seq) return;
      renderAll(result);
    });
  }

  window.StartAppDownload = { init: init };
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
