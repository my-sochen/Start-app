"use strict";
/*
 * Guide behavior: local chapter search and deep-link reveal. init() is
 * idempotent and is called again by the client-side router after the guide is
 * swapped in. Window-level listeners are registered once and read the live DOM.
 */
(function () {
  function parts() {
    var input = document.querySelector("#doc-search");
    if (!input) return null;
    return {
      input: input,
      sections: Array.prototype.slice.call(document.querySelectorAll("[data-doc]")),
      status: document.querySelector("#search-status"),
    };
  }
  function applySearch() {
    var p = parts();
    if (!p) return;
    var query = p.input.value.trim().toLocaleLowerCase("he");
    var count = 0;
    p.sections.forEach(function (section) {
      var visible =
        !query || section.textContent.toLocaleLowerCase("he").includes(query);
      section.hidden = !visible;
      if (visible) count++;
      var link = document.querySelector('.sidebar a[href="#' + section.id + '"]');
      if (link) link.hidden = !visible;
    });
    document.querySelector("#no-results").hidden = count !== 0;
    p.status.textContent = query
      ? count + " פרקים מתוך " + p.sections.length
      : p.sections.length + " פרקי מדריך";
  }
  function showHash() {
    var p = parts();
    if (!p || !location.hash) return;
    var target = document.getElementById(decodeURIComponent(location.hash.slice(1)));
    if (target && target.hidden) {
      p.input.value = "";
      applySearch();
      target.scrollIntoView();
    }
  }
  function init() {
    var p = parts();
    if (!p) return;
    p.input.addEventListener("input", applySearch);
    applySearch();
    showHash();
  }
  window.addEventListener("hashchange", showHash);
  window.addEventListener("beforeprint", function () {
    var p = parts();
    if (p) p.sections.forEach(function (s) { s.hidden = false; });
  });
  window.addEventListener("afterprint", applySearch);
  window.StartAppDocs = { init: init };
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
