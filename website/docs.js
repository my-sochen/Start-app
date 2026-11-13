"use strict";
const input = document.querySelector("#doc-search");
const sections = [...document.querySelectorAll("[data-doc]")];
const status = document.querySelector("#search-status");
function applySearch() {
  const query = input.value.trim().toLocaleLowerCase("he");
  let count = 0;
  for (const section of sections) {
    const visible =
      !query || section.textContent.toLocaleLowerCase("he").includes(query);
    section.hidden = !visible;
    if (visible) count++;
    const link = document.querySelector(
      '.sidebar a[href="#' + section.id + '"]',
    );
    if (link) link.hidden = !visible;
  }
  document.querySelector("#no-results").hidden = count !== 0;
  status.textContent = query
    ? count + " פרקים מתוך " + sections.length
    : sections.length + " פרקי תיעוד";
}
input.addEventListener("input", applySearch);
function showHash() {
  if (location.hash) {
    const target = document.getElementById(
      decodeURIComponent(location.hash.slice(1)),
    );
    if (target && target.hidden) {
      input.value = "";
      applySearch();
      target.scrollIntoView();
    }
  }
}
window.addEventListener("hashchange", showHash);
window.addEventListener("beforeprint", () => {
  for (const s of sections) s.hidden = false;
});
window.addEventListener("afterprint", applySearch);
applySearch();
showHash();
