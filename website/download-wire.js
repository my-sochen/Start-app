"use strict";
/*
 * Activation wiring for the prototype download pointer (website lane proposal).
 * Loads the same-origin pointer document (pointer.json, deployed atomically
 * with this site by the pointer workflow) and renders both download CTAs via
 * StartAppPointer. Every non-ok result keeps the honest disabled state - this
 * file adds no state of its own beyond the reader contract.
 */
(function () {
  function wire() {
    if (!window.StartAppPointer) return;
    // Same-origin resolution: pointer.json sits next to this page in the
    // deployed artifact; an absolute https URL satisfies load()'s own check.
    var url = new URL("pointer.json", window.location.href).href;
    var hero = document.querySelector(".hero-download-button");
    var heroNote = document.getElementById("hero-download-state");
    var main = document.querySelector("#download button.primary");
    var mainNote = document.getElementById("download-state");
    window.StartAppPointer.load(url).then(function (result) {
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
