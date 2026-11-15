"use strict";
/*
 * Activation wiring for the prototype download pointer (website lane proposal).
 * Loads the same-origin pointer document (pointer.json, deployed atomically
 * with this site by the pointer workflow) and renders both download CTAs via
 * StartAppPointer. Also resolves the state-bearing static copy (availability
 * badge and download-section heading) so the page never contradicts the live
 * state: ok / none (incl. withdrawal) / invalid each get truthful text, and
 * the initial static copy is the truthful "checking" state. This file adds no
 * state of its own beyond the reader contract.
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

  function applyStaticState(state) {
    var texts = TEXTS[state] || TEXTS.invalid;
    var badge = document.getElementById("availability-badge");
    var heading = document.getElementById("download-heading");
    if (badge) badge.textContent = texts.badge;
    if (heading) heading.textContent = texts.heading;
  }

  // With JavaScript running we CAN check: upgrade the static no-JS state to
  // the truthful "checking" state before the fetch resolves it. Without
  // JavaScript the page keeps its shipped state, which is the honest
  // "cannot verify - download stays off" copy.
  function applyLoading() {
    var badge = document.getElementById("availability-badge");
    var hero = document.querySelector(".hero-download-button");
    var heroNote = document.getElementById("hero-download-state");
    var main = document.querySelector("#download button.primary");
    var mainNote = document.getElementById("download-state");
    if (badge) badge.textContent = "בודק זמינות הורדה…";
    if (hero) {
      var label = hero.querySelector("span");
      if (label) label.textContent = "בודק זמינות הורדה…";
    }
    if (main) main.textContent = "בודק זמינות הורדה…";
    if (heroNote) heroNote.textContent = "יעד האריזה: Windows x64. בודק אם קיימת גרסת אב-טיפוס מאומתת…";
    if (mainNote) mainNote.textContent = "בודק אם קיימת גרסת אב-טיפוס מאומתת…";
  }

  function wire() {
    if (!window.StartAppPointer) {
      applyStaticState("invalid");
      return;
    }
    applyLoading();
    // Same-origin resolution: pointer.json sits next to this page in the
    // deployed artifact; an absolute https URL satisfies load()'s own check.
    var url = new URL("pointer.json", window.location.href).href;
    var hero = document.querySelector(".hero-download-button");
    var heroNote = document.getElementById("hero-download-state");
    var main = document.querySelector("#download button.primary");
    var mainNote = document.getElementById("download-state");
    window.StartAppPointer.load(url).then(function (result) {
      applyStaticState(result.state);
      if (main) {
        window.StartAppPointer.render({ button: main, note: mainNote }, result);
      }
      if (hero) {
        window.StartAppPointer.render(
          { button: hero, note: heroNote, label: hero.querySelector("span") },
          result
        );
      }
    });
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", wire);
  } else {
    wire();
  }
})();
