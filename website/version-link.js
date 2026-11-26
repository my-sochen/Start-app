"use strict";
/* Deep-link only to the requested, validated inventory row. Never select latest
 * as a substitute. The native #download anchor remains useful without JS. */
(function () {
  function locate() {
    const requested = new URLSearchParams(location.search).get("version");
    const inventory = document.querySelector("[data-prototype-offers]");
    if (!requested || !inventory) return;
    const details = inventory.closest("details");
    if (details) details.open = true;
    let note = document.querySelector("[data-version-request-note]");
    if (!note) {
      note = document.createElement("p");
      note.dataset.versionRequestNote = "";
      note.className = "version-request-note";
      note.setAttribute("role", "status");
      inventory.before(note);
    }
    const row = Array.from(inventory.querySelectorAll("[data-tag]")).find(el => el.dataset.tag === requested);
    inventory.querySelectorAll(".version-target").forEach(el => el.classList.remove("version-target"));
    if (row) {
      note.hidden = true;
      row.classList.add("version-target");
      row.setAttribute("tabindex", "-1");
      row.scrollIntoView({block: "center", behavior: "instant"});
      row.focus({preventScroll: true});
    } else {
      const text = "הגרסה המבוקשת אינה מוצעת כרגע ברשימת הגרסאות.";
      note.textContent = window.StartAppI18n ? window.StartAppI18n.t(text) : text;
      note.hidden = false;
      note.scrollIntoView({block: "center", behavior: "instant"});
    }
  }
  document.addEventListener("startapp:download-settled", locate);
})();
