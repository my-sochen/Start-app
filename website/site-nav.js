"use strict";
function tr(text) { return typeof window !== "undefined" && window.StartAppI18n ? window.StartAppI18n.t(text) : text; }

/*
 * Progressive-enhancement client-side navigation between the two real pages
 * (index.html, docs.html) of this site directory. Every link keeps its real
 * href, so with no JavaScript, a failed fetch, a new-tab/modifier click, or a
 * crawler, the browser performs a normal full page load to the same URL.
 *
 * With JavaScript: same-origin clicks between the two pages fetch the target
 * HTML, swap <main>, the review strip, the title, canonical, description and
 * reciprocal hreflang alternates,
 * push history, re-run the page initializers (download CTAs via the cached
 * validated pointer result, guide search), reset or restore scroll, move
 * focus to the new page heading and announce it in a polite live region.
 * Back/forward restore the scroll saved for each history entry (saved as the
 * user scrolls, so hash-link entries are covered too). Any failure falls back
 * to a real navigation. No inline script, no eval, same-origin fetch only.
 */
(function () {
  var PAGES = { "index.html": "index.html", "docs.html": "docs.html", "": "index.html" };
  var cache = {};
  var token = 0;
  var saveTimer = null;
  // Latest scroll position a Back/Forward navigation restored whose page is
  // still settling: post-swap the download CTA re-validates fail-closed, then
  // its details panel un-hides, growing content above the restored spot, and
  // scroll anchoring drifts it. The drift surfaces as scroll events, so the
  // saved position is re-applied while this restore is pending. A pending
  // restore ends at the first of three real events: this navigation's CTA
  // render lifecycle settling ("startapp:download-settled" with this token),
  // the user's first real scroll or focus input, or any newer navigation. One
  // singleton - no timers, no accumulating listeners, no guessing when async
  // work finishes.
  var pendingRestore = null;
  function noteUserScroll() { if (pendingRestore) pendingRestore.userScrolled = true; }
  addEventListener("wheel", noteUserScroll, { passive: true });
  addEventListener("touchmove", noteUserScroll, { passive: true });
  addEventListener("keydown", function (e) {
    if ([" ", "PageUp", "PageDown", "ArrowUp", "ArrowDown", "Home", "End"].indexOf(e.key) !== -1) noteUserScroll();
  });
  // Focus movement is navigation intent too: keyboard tabbing, assistive tech
  // and programmatic focus must be allowed to scroll to the focused element
  // even while a restore is pending. The router's own post-swap heading focus
  // (preventScroll) is marked so it does not cancel its own restore.
  var internalFocus = false;
  document.addEventListener("focusin", function () {
    if (!internalFocus) noteUserScroll();
  });
  // Site directory (e.g. "/Start-app/"), taken once from the page that loaded
  // this script. A same-named file in another directory is not enhanced.
  var BASE = location.pathname.replace(/[^\/]*$/, "");

  function pageOf(url) {
    var u = new URL(url, location.href);
    if (u.origin !== location.origin) return null;
    if (u.pathname.slice(0, BASE.length) !== BASE) return null;
    var name = u.pathname.slice(BASE.length);
    return Object.prototype.hasOwnProperty.call(PAGES, name) ? PAGES[name] : null;
  }
  function currentPage() {
    return pageOf(location.href);
  }

  function fetchPage(page) {
    if (cache[page]) return Promise.resolve(cache[page]);
    var url = new URL(page, location.href).href;
    return fetch(url, { credentials: "omit" }).then(function (res) {
      if (!res.ok) throw new Error("HTTP " + res.status);
      return res.text();
    }).then(function (html) {
      var doc = new DOMParser().parseFromString(html, "text/html");
      if (!doc.querySelector("main#main")) throw new Error("no main");
      cache[page] = doc;
      return doc;
    });
  }

  function announce(text) {
    var el = document.getElementById("route-announcer");
    if (!el) return;
    el.textContent = "";
    // Re-set on the next task so repeated identical announcements are read.
    setTimeout(function () { el.textContent = text; }, 30);
  }

  function setCurrentNav(page) {
    var links = document.querySelectorAll("header > nav a");
    Array.prototype.forEach.call(links, function (a) {
      var u = new URL(a.getAttribute("href"), location.href);
      var isHere = !u.hash && pageOf(u.href) === page;
      if (isHere) a.setAttribute("aria-current", "page");
      else a.removeAttribute("aria-current");
    });
  }

  function copyChildren(from, to) {
    to.replaceChildren.apply(to, Array.prototype.map.call(from.childNodes, function (n) {
      return document.importNode(n, true);
    }));
  }

  function swapIn(doc, page) {
    document.title = doc.title;
    [["link[rel=\"canonical\"]", "href"], ["meta[name=\"description\"]", "content"]].forEach(function (p) {
      var cur = document.head.querySelector(p[0]);
      var nxt = doc.head.querySelector(p[0]);
      if (cur && nxt) cur.setAttribute(p[1], nxt.getAttribute(p[1]));
    });
    // Copy the destination's full reciprocal language set, including x-default.
    // Replacing the set also removes stale links if a later page changes its locales.
    document.head.querySelectorAll('link[rel="alternate"][hreflang]').forEach(function (link) {
      link.remove();
    });
    doc.head.querySelectorAll('link[rel="alternate"][hreflang]').forEach(function (link) {
      document.head.appendChild(document.importNode(link, true));
    });
    var main = document.getElementById("main");
    main.className = doc.getElementById("main").className;
    copyChildren(doc.getElementById("main"), main);
    // Keep the guide jump bar beside the persistent header during page swaps.
    // Their common sticky wrapper sizes itself naturally, even if nav wraps.
    var header = document.querySelector("header");
    var pinned = document.querySelector(".pinned-top");
    var nextJump = doc.querySelector(".pinned-top > .docs-jump");
    if (nextJump) {
      if (!pinned) {
        pinned = document.createElement("div");
        pinned.className = "pinned-top";
        header.parentNode.insertBefore(pinned, header);
        pinned.appendChild(header);
      }
      var oldJump = pinned.querySelector(".docs-jump");
      if (oldJump) oldJump.remove();
      pinned.appendChild(document.importNode(nextJump, true));
    } else if (pinned) {
      pinned.parentNode.insertBefore(header, pinned);
      pinned.remove();
    }
    var strip = document.querySelector(".review-strip");
    var nextStrip = doc.querySelector(".review-strip");
    if (strip && nextStrip) copyChildren(nextStrip, strip);
    var languageMenu = document.querySelector(".language-switch");
    var nextLanguageMenu = doc.querySelector(".language-switch");
    if (languageMenu && nextLanguageMenu) copyChildren(nextLanguageMenu, languageMenu);
    setCurrentNav(page);
    document.body.setAttribute("data-page", page);
    if (window.StartAppDocs) window.StartAppDocs.init();
  }

  function afterSwap(url, restoreY) {
    var u = new URL(url, location.href);
    var target = u.hash ? document.getElementById(decodeURIComponent(u.hash.slice(1))) : null;
    pendingRestore = null;
    if (typeof restoreY === "number") {
      window.scrollTo({ top: restoreY, left: 0, behavior: "instant" });
      // Only arm the correction when this page run will signal settlement;
      // without the CTA lifecycle there is no async re-render to defend
      // against and nothing would ever clear the pending state.
      if (window.StartAppDownload) pendingRestore = { token: token, y: restoreY, userScrolled: false };
    } else if (target) {
      target.scrollIntoView({ behavior: "instant" });
    } else {
      window.scrollTo({ top: 0, left: 0, behavior: "instant" });
    }
    var focusEl = target || document.querySelector("main h1");
    if (focusEl) {
      if (!focusEl.hasAttribute("tabindex")) focusEl.setAttribute("tabindex", "-1");
      internalFocus = true;
      try {
        focusEl.focus({ preventScroll: true });
      } finally {
        internalFocus = false;
      }
    }
    var pageHeading = document.querySelector("main h1");
    announce(tr("נטען: ") + (pageHeading ? pageHeading.textContent.trim().replace(/\s+/g, " ") : document.title));
    // Re-validate the CTAs only after the swap's scroll/focus state (and any
    // armed pending restore) exists: a terminal render path that settles
    // synchronously (the missing-StartAppPointer fail-closed path) dispatches
    // its settlement immediately, and it must find the restore already armed
    // instead of being lost before it.
    if (window.StartAppDownload) window.StartAppDownload.init(token);
  }

  function flushScroll() {
    if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
    history.replaceState({ y: window.scrollY }, "");
  }

  function go(url, push, restoreY) {
    var page = pageOf(url);
    var my = ++token;
    fetchPage(page).then(function (doc) {
      if (my !== token) return;
      if (push) {
        flushScroll();
        history.pushState({ y: 0 }, "", url);
      }
      swapIn(doc, page);
      afterSwap(url, restoreY);
    }).catch(function () {
      if (my !== token) return;
      location.assign(url);
    });
  }

  document.addEventListener("click", function (e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    var a = e.target.closest && e.target.closest("a[href]");
    if (!a || (a.target && a.target !== "_self") || a.hasAttribute("download")) return;
    var page = pageOf(a.href);
    if (!page) return;
    var u = new URL(a.href);
    if (page === currentPage()) {
      // Clicking a link to the page already shown cancels a pending
      // navigation to the other page (the user changed their mind).
      token++;
      // In-page hash link: the browser creates a new history entry right after
      // this handler, so persist the DEPARTING entry's scroll first (no timer).
      if (u.hash) flushScroll();
      // Same page, same query, no hash: scroll to top in place. Hash links
      // and links with a different query are left to the browser.
      if (!u.hash && u.search === location.search) {
        e.preventDefault();
        window.scrollTo({ top: 0, left: 0, behavior: "instant" });
      }
      return;
    }
    // Only exact site URLs are enhanced; a query string is never carried by
    // the swap, so such links navigate natively and stay URL/content-true.
    if (u.search) return;
    e.preventDefault();
    go(u.pathname + u.search + u.hash, true);
  });

  window.addEventListener("popstate", function (e) {
    token++;
    var page = pageOf(location.href);
    if (!page) { location.reload(); return; }
    var y = e.state && typeof e.state.y === "number" ? e.state.y : undefined;
    if (page === document.body.getAttribute("data-page")) {
      // Same page (hash step). scrollRestoration is manual, so restore the
      // position saved for that entry.
      if (typeof y === "number") window.scrollTo({ top: y, left: 0, behavior: "instant" });
      return;
    }
    go(location.pathname + location.search + location.hash, false, y);
  });

  // Save scroll into the CURRENT history entry as the user scrolls
  // (throttled), so Back/Forward can restore any entry, including entries
  // created by in-page hash links.
  window.addEventListener("scroll", function () {
    if (saveTimer === null) saveTimer = setTimeout(function () { saveTimer = null; history.replaceState({ y: window.scrollY }, ""); }, 120);
  }, { passive: true });

  // While a restore is pending, re-apply its saved position whenever async
  // post-swap growth (the CTA panel un-hide) drifts it via scroll anchoring.
  window.addEventListener("scroll", function () {
    if (!pendingRestore || pendingRestore.userScrolled || pendingRestore.token !== token) return;
    if (Math.abs(window.scrollY - pendingRestore.y) <= 1) return;
    window.scrollTo({ top: pendingRestore.y, left: 0, behavior: "instant" });
  }, { passive: true });

  // This navigation's CTA render lifecycle settled: no more async growth from
  // this swap is coming, so the restore stops being defended. Later focus
  // moves, hash navigation and programmatic scrolls are free again.
  document.addEventListener("startapp:download-settled", function (e) {
    if (!pendingRestore || !e.detail || e.detail.token !== pendingRestore.token || e.detail.token !== token) return;
    if (!pendingRestore.userScrolled && Math.abs(window.scrollY - pendingRestore.y) > 1) {
      window.scrollTo({ top: pendingRestore.y, left: 0, behavior: "instant" });
    }
    pendingRestore = null;
  });

  document.body.setAttribute("data-page", currentPage() || "");
  if ("scrollRestoration" in history) history.scrollRestoration = "manual";
})();

/* Gentle native depth drift for the dedicated line art. One scroll listener
 * publishes two CSS variables: --depth-y keeps the accepted half-speed
 * modulo-720 contract so a host adapter can drive its own backdrop from its
 * native scroll container; --art-y is the same continuous half-speed lag,
 * unclamped, driving the restored top/bottom illustrations. The art exists
 * only on the home page, whose real scroll span is short (roughly 120-160px
 * on phones, near zero on desktop), so geometry bounds the travel to a few
 * dozen pixels without any artificial cap. Native rAF-throttled listener,
 * no CSS scroll-timeline dependency. Transform-only; reduced-motion -> 0. */
(function () {
  "use strict";
  var motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  var queued = false;
  function update() {
    queued = false;
    var sc = Math.max(0, window.scrollY);
    var y = motion.matches ? 0 : -((sc * 0.5) % 720);
    var ay = motion.matches ? 0 : sc * 0.5;
    document.body.style.setProperty("--depth-y", y + "px");
    document.body.style.setProperty("--art-y", ay + "px");
  }
  function requestUpdate() {
    if (!queued) { queued = true; window.requestAnimationFrame(update); }
  }
  window.addEventListener("scroll", requestUpdate, { passive: true });
  window.addEventListener("resize", requestUpdate, { passive: true });
  window.addEventListener("pageshow", requestUpdate);
  if (motion.addEventListener) motion.addEventListener("change", requestUpdate);
  else if (motion.addListener) motion.addListener(requestUpdate);
  update();
})();

/* Adaptive clearance for the desktop guide chapter list: the pinned header's
 * real height varies (nav wrapping on narrow widths, enlarged text, native
 * zoom), and a fixed offset can dock the list's top under it. Measure the
 * pinned bar and publish --pinned-top-h; styles.css docks the sidebar below
 * it. Fallback in CSS reproduces the base 85px header when JS is off. */
(function () {
  "use strict";
  function bar() { return document.querySelector(".pinned-top") || document.querySelector("header"); }
  function publish() {
    var el = bar();
    if (el) document.body.style.setProperty("--pinned-top-h", el.getBoundingClientRect().height + "px");
  }
  if ("ResizeObserver" in window) {
    var ro = new ResizeObserver(publish);
    // The <header> element persists across client-side page swaps (it is
    // reparented in and out of .pinned-top, never recreated). Observing the
    // wrapper instead goes stale on the detached node after Guide->Home, and
    // the returned Guide gets a NEW wrapper the observer never saw - the
    // variable then keeps a pre-swap height and the dock slips under the
    // real bar. publish() always measures the CURRENT bar.
    var header = document.querySelector("header");
    if (header) ro.observe(header);
  }
  window.addEventListener("resize", publish, { passive: true });
  window.addEventListener("load", publish);
  window.addEventListener("popstate", publish);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(publish);
  document.addEventListener("DOMContentLoaded", publish);
  publish();
})();
