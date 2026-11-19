#!/usr/bin/env python3
"""Served-identity verifier for the Pages deployment (trampoline option A, v2).

Runs in the writer job AFTER deploy-pages, while the job still holds the
"pages" concurrency lock, so no second writer of this workflow can interleave
a deployment during verification. Compares the LIVE served bytes of
pointer.json and downloads.json against the EXACT STAGED bytes frozen from
_site before upload (see the workflow's "Freeze staged identity bytes" step).
Expected values are never regenerated: pointer.json carries generated_utc, so
a re-render would never match what was actually deployed.

Semantics:
- Exact staged bytes (sha256) AND schema/consistency checks must both hold.
  Byte equality alone trusts a corrupt staged artifact; schema alone misses
  stale/foreign bytes.
- BOTH staged documents are mandatory (pointer.json and downloads.json). The
  workflow always stages the historical catalog; a missing one is a staged
  error (exit 2), never a silently skipped check.
- Staged and served documents are validated with TYPED rules taken from the
  actual generator contract (pick_release_pointer.pointer_document and
  catalog_projection.entry_for/project): exact integer schema 1 (a boolean is
  not 1), prototype tag/version/commit/asset/url shapes, nullable sha256 and
  size (formats enforced only when non-null), layout in the generator's set,
  legacy-flat-v1 only for the generator's legacy pin, catalog ordering,
  uniqueness and pointer/catalog identity agreement. The constants below
  mirror the pinned generator; a drift test compares them when the generator
  is importable.
- Legitimate null selection is first-class: a staged pointer with
  candidate:null must be served byte-exactly with an empty catalog.
- Rollback/withdrawal is first-class: a staged pointer naming a LOWER proto
  build than whatever was previously live passes when served byte-exactly.
- Different well-formed served bytes are reported as such and never silently
  accepted; any mismatch that survives the deadline fails the job.
- URL policy: the base URL must be exactly the official Pages project site of
  the fixed repository: https, host <owner>.github.io, path /<repo>/ , no
  userinfo, no explicit port (including443), no query, fragment or params. Anything else
  (including a custom domain) is rejected with exit 2 before any request.
  Redirects are refused outright: the served URL is requested exactly and a
  redirect (host, scheme or path change) is a finding, not followed.
- ABSOLUTE deadline (--deadline seconds, monotonic clock) covers sleeps AND
  request time for accepting results. No request starts with < 1 s left;
  requested socket timeout is min(REQUEST_TIMEOUT_SECONDS, remaining).
  Sleeps are capped to the remaining time. A served result is accepted ONLY inside the deadline:
  the remaining budget is re-checked after every fetch (post-fetch check),
  the request wall is re-checked after every read (post-read check) and at
  body EOF (EOF check), and the budget is checked once more before any
  success is returned (final-success check). A response that lands at or
  after the deadline FAILS CLOSED; it can never be accepted as a match.
- Request timing limits: socket timeouts are idle-operation bounds, not a
  universal whole-request wall guarantee. Before each body read we try to
  shrink the socket timeout to the remaining request budget. Buffered reads,
  DNS, TLS and response headers may still block beyond that budget; late
  returned data is rejected by post-read/post-fetch/final-success checks.
  Unknown response-stack shapes make socket adjustment a no-op. The
  whole-process SIGALRM (deadline + request timeout + grace) and workflow
  step timeout are the hard backstops for stuck calls. No credentials or
  cache-busting query are used.
- NO automatic redeploy. Mismatch fails loudly (non-zero exit) for the owner.

Offline-testable: verify() and check_once() take an injected fetcher, clock
and sleeper; tests never touch the network.

Usage:
  verify_served.py --base-url URL --expected DIR [--interval S] [--deadline S]
Exit codes: 0 = served identity matches staged bytes; 1 = mismatch/timeout
after the absolute deadline; 2 = staged files or base URL invalid.
"""
import argparse, hashlib, json, os, re, signal, sys, time
import urllib.error, urllib.request
from urllib.parse import urlsplit

# --- constants mirroring the pinned generator (catalog_projection / selector) ---
REPO = "my-sochen/Start-app"
ASSET = "StartApp-win-x64.zip"
TAG_RE = re.compile(r"v([0-9]+\.[0-9]+\.[0-9]+-proto\.([0-9]+))$")
SHA_RE = re.compile(r"[0-9a-f]{40}")
DIGEST_RE = re.compile(r"[0-9a-f]{64}")
SAFE_INT = 9007199254740991
MAX_RELEASES = 1000
LAYOUTS = ("portable-v1", "legacy-flat-v1", "unknown")
LEGACY = ("v0.1.0-proto.1", "db94adc19056b6dd68d338c030c6ce4983a3057c",
          "7e8ee4d53bcba3626543aebae97c08c5bfc41057582af1e03f3762f68ec6c702", 65356245)
IDENTITY = ("schema", "channel", "label", "version", "tag", "commit", "asset", "url", "size", "sha256", "layout")
GENERATED_RE = re.compile(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")

POINTER_MAX_BYTES = 1024 * 1024          # matches catalog_projection.MAX_JSON_BYTES
CATALOG_MAX_BYTES = 4 * 1024 * 1024      # matches catalog_projection.MAX_CATALOG_BYTES
FILES = (("pointer.json", POINTER_MAX_BYTES), ("downloads.json", CATALOG_MAX_BYTES))

REQUEST_TIMEOUT_SECONDS = 30
READ_CHUNK = 64 * 1024
MIN_PROBE_SECONDS = 1
HARD_STOP_GRACE_SECONDS = 10
MAX_DEADLINE_SECONDS = 3600

OFFICIAL_HOST = REPO.split("/")[0].lower() + ".github.io"
PROJECT_PATH = "/" + REPO.split("/")[1] + "/"


class Problem(Exception):
    """One bounded-check finding; retryable until the deadline."""


class StagedError(Exception):
    """The staged expected files cannot be used (exit 2)."""


def _sha256(b):
    return hashlib.sha256(b).hexdigest()


# ---------------------------------------------------------------- URL policy
def validate_base_url(url):
    """Return the canonical base (with trailing slash) or raise ValueError.
    Strict allowlist: exactly https://<owner>.github.io/<repo>[/]."""
    if not isinstance(url, str) or not url or len(url) > 200:
        raise ValueError("base-url missing or too long")
    if any(ord(c) <= 0x20 or ord(c) >= 0x7f or c == "\\" for c in url):
        raise ValueError("base-url contains whitespace, control, non-ASCII or backslash characters")
    parts = urlsplit(url)
    if parts.scheme != "https":
        raise ValueError("base-url must be https")
    if "@" in parts.netloc:
        raise ValueError("base-url must not carry userinfo")
    if parts.netloc != OFFICIAL_HOST and parts.netloc.lower() != OFFICIAL_HOST:
        raise ValueError("base-url host is not the official Pages host (%s)" % OFFICIAL_HOST)
    if parts.query or "?" in url:
        raise ValueError("base-url must not carry a query")
    if parts.fragment or "#" in url:
        raise ValueError("base-url must not carry a fragment")
    if ";" in parts.path:
        raise ValueError("base-url must not carry path params")
    if parts.path not in (PROJECT_PATH, PROJECT_PATH.rstrip("/")):
        raise ValueError("base-url path is not exactly the project path %s" % PROJECT_PATH)
    return "https://" + OFFICIAL_HOST + PROJECT_PATH


class _RefuseRedirects(urllib.request.HTTPRedirectHandler):
    """Explicit redirect policy: none. The exact official URL is requested;
    any redirect (cross-host, scheme change, or same-host path change) is
    refused and surfaces as a retryable finding."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise urllib.error.HTTPError(newurl, code, "redirect refused by policy", headers, fp)


def read_capped(read, cap, deadline, clock=time.monotonic, set_timeout=None):
    """Read capped chunks, reject late results, and try to tighten idle timeouts.
    A transport read may still block beyond its budget. Socket adjustment is
    best-effort, not a whole-read wall guarantee; the process/step backstops
    bound stuck calls. Post-read and EOF checks reject late returned data."""
    chunks, total = [], 0
    while True:
        remaining = deadline - clock()
        if remaining <= 0:                       # pre-read check
            raise Problem("request exceeded its wall-clock bound while reading")
        if set_timeout is not None:
            set_timeout(remaining)               # try to tighten idle timeout before this read
        chunk = read(min(READ_CHUNK, cap + 1 - total))
        if not chunk:
            if clock() > deadline:               # EOF check
                raise Problem("request reached EOF only after its wall-clock bound")
            return b"".join(chunks)
        if clock() > deadline:                   # post-read check (blocking read overran)
            raise Problem("request exceeded its wall-clock bound during a read")
        total += len(chunk)
        if total > cap:
            raise Problem("body exceeds %d-byte bound" % cap)
        chunks.append(chunk)


def _set_read_wall(resp, seconds):
    """Best-effort idle socket timeout adjustment, not a whole-read wall cap:
    walk the urllib response stack (addinfourl/HTTPError -> HTTPResponse ->
    buffered reader -> SocketIO) to the raw socket and shrink its timeout to
    the remaining request wall. Returns False (no-op) if the stack shape is
    not the expected one; the wall-clock checks in read_capped still apply."""
    cur, seen = resp, set()
    while cur is not None and id(cur) not in seen:
        seen.add(id(cur))
        sock = getattr(cur, "_sock", None)
        if sock is not None and hasattr(sock, "settimeout"):
            try:
                sock.settimeout(max(0.001, seconds))
            except OSError:
                return False
            return True
        cur = getattr(cur, "fp", None) or getattr(cur, "raw", None)
    return False


def http_fetcher(base_url, clock=time.monotonic):
    """Build the live fetcher. fetch(name, cap, timeout) -> (status, body, age).
    Raises ValueError for a base URL outside the allowlist."""
    base = validate_base_url(base_url)
    opener = urllib.request.build_opener(_RefuseRedirects())

    def fetch(name, cap, timeout):
        url = base + name
        req = urllib.request.Request(url, headers={"User-Agent": "served-identity-verifier",
                                                   "Accept": "application/json"})
        req_deadline = clock() + timeout
        try:
            with opener.open(req, timeout=timeout) as r:
                status, headers = r.status, r.headers
                if r.geturl() != url:
                    raise Problem("%s: served from a different URL than requested" % name)
                body = read_capped(r.read, cap, req_deadline, clock,
                                   set_timeout=lambda s: _set_read_wall(r, s))
        except urllib.error.HTTPError as e:
            status, headers = e.code, e.headers
            body = read_capped(e.read, cap, req_deadline, clock,
                               set_timeout=lambda s: _set_read_wall(e, s))
        except Problem as e:
            raise Problem("%s: %s" % (name, e))
        return status, body, headers.get("Age") if headers else None

    return fetch


# ------------------------------------------------------------ JSON + schema
def _no_const(c):
    raise ValueError("non-standard JSON constant " + c)


def _unique_pairs(pairs):
    keys = [k for k, _ in pairs]
    if len(keys) != len(set(keys)):
        raise ValueError("duplicate JSON key")
    return dict(pairs)


def _parse(doc_bytes, name):
    try:
        return json.loads(doc_bytes.decode("utf-8"), parse_constant=_no_const, object_pairs_hook=_unique_pairs)
    except Exception:  # includes RecursionError, UnicodeDecodeError, ValueError
        raise Problem("%s: body is not valid UTF-8 JSON" % name)


def _is_int(v):
    return type(v) is int


def _nullable_error(doc, what):
    """sha256 and size are legitimately nullable (manifest may omit them);
    formats apply only when non-null."""
    sha = doc.get("sha256")
    if sha is not None and not (isinstance(sha, str) and DIGEST_RE.fullmatch(sha)):
        return "%s sha256 is not null or 64 lowercase hex" % what
    size = doc.get("size")
    if size is not None and not (_is_int(size) and 0 < size <= SAFE_INT):
        return "%s size is not null or a positive browser-safe integer" % what
    return None


def identity_error(doc, what):
    """Typed check of one selected-release identity (pointer or catalog entry)."""
    for k in IDENTITY:
        if k not in doc:
            return "%s missing identity field %s" % (what, k)
    if not (_is_int(doc["schema"]) and doc["schema"] == 1):
        return "%s schema is not integer 1" % what
    if doc["channel"] != "prototype":
        return "%s channel is not 'prototype'" % what
    if not isinstance(doc["label"], str):
        return "%s label is not a string" % what
    tag = doc["tag"]
    m = TAG_RE.fullmatch(tag) if isinstance(tag, str) else None
    if not m:
        return "%s tag is not a prototype tag vX.Y.Z-proto.N" % what
    if doc["version"] != m.group(1):
        return "%s version does not match its tag" % what
    if not (isinstance(doc["commit"], str) and SHA_RE.fullmatch(doc["commit"])):
        return "%s commit is not a full 40-char sha" % what
    if doc["asset"] != ASSET:
        return "%s asset is not %s" % (what, ASSET)
    if doc["url"] != "https://github.com/" + REPO + "/releases/download/" + tag + "/" + ASSET:
        return "%s url is not the release asset URL for its tag" % what
    err = _nullable_error(doc, what)
    if err:
        return err
    layout = doc["layout"]
    if layout not in LAYOUTS or not isinstance(layout, str):
        return "%s layout is not one of %s" % (what, list(LAYOUTS))
    if layout == "legacy-flat-v1" and (doc["tag"], doc["commit"], doc["sha256"], doc["size"]) != LEGACY:
        return "%s layout legacy-flat-v1 is only valid for the legacy pin" % what
    return None


def pointer_schema_error(doc):
    if not isinstance(doc, dict):
        return "pointer is not a JSON object"
    if not (_is_int(doc.get("schema")) and doc["schema"] == 1):
        return "pointer schema is not integer 1"
    if doc.get("channel") != "prototype":
        return "pointer channel is not 'prototype'"
    if "candidate" in doc:
        if doc["candidate"] is not None:
            return "pointer candidate must be null when present"
        if not (isinstance(doc.get("reason"), str) and doc["reason"]):
            return "null-selection pointer lacks a string reason"
        return None
    err = identity_error(doc, "pointer")
    if err:
        return err
    if not (isinstance(doc.get("generated_utc"), str) and GENERATED_RE.fullmatch(doc["generated_utc"])):
        return "pointer generated_utc is not a UTC timestamp"
    if not isinstance(doc.get("scope"), str):
        return "pointer scope is not a string"
    return None


def catalog_schema_error(doc):
    if not isinstance(doc, dict):
        return "catalog is not a JSON object"
    if not (_is_int(doc.get("schema")) and doc["schema"] == 1):
        return "catalog schema is not integer 1"
    if doc.get("channel") != "prototype":
        return "catalog channel is not 'prototype'"
    if doc.get("complete") is not True:
        return "catalog complete is not true"
    entries = doc.get("entries")
    if not isinstance(entries, list) or len(entries) > MAX_RELEASES:
        return "catalog entries is not a bounded list"
    if "selected_tag" not in doc:
        return "catalog selected_tag is missing"
    selected = doc["selected_tag"]
    if selected is not None and not (isinstance(selected, str) and TAG_RE.fullmatch(selected)):
        return "catalog selected_tag must be a prototype tag or null"
    if (selected is None) != (len(entries) == 0):
        return "catalog selected_tag is null exactly when entries is empty"
    tags, builds = set(), []
    for i, entry in enumerate(entries):
        if not isinstance(entry, dict):
            return "catalog entry %d is not an object" % i
        err = identity_error(entry, "catalog entry %d" % i)
        if err:
            return err
        if entry["tag"] in tags:
            return "catalog has a duplicate tag"
        tags.add(entry["tag"])
        builds.append(int(TAG_RE.fullmatch(entry["tag"]).group(2)))
    if len(set(builds)) != len(builds):
        return "catalog has a duplicate proto build"
    if builds != sorted(builds, reverse=True):
        return "catalog entries are not ordered by descending build"
    if entries and entries[0]["tag"] != selected:
        return "catalog selected_tag is not its first (highest build) entry"
    return None


def pair_error(pointer, catalog):
    """Consistency between the two mandatory documents (parsed, each valid)."""
    if pointer.get("candidate", "present") is None:
        if catalog["selected_tag"] is not None or catalog["entries"]:
            return "catalog disagrees with null pointer"
        return None
    if catalog["selected_tag"] != pointer["tag"]:
        return "catalog selected_tag != pointer tag"
    first = catalog["entries"][0]
    for k in IDENTITY:
        if first.get(k) != pointer.get(k):
            return "catalog selected entry %s != pointer %s" % (k, k)
    return None


def validate_staged(expected):
    """Fail-fast (exit 2) checks on the frozen staged files themselves, plus
    pointer/catalog agreement. Returns None or an error string. Never raises
    for bad staged content."""
    for name, cap in FILES:
        if name not in expected:
            return "staged %s is missing (both staged documents are required)" % name
        if len(expected[name]) > cap:
            return "staged %s exceeds its %d-byte cap" % (name, cap)
    try:
        pointer = _parse(expected["pointer.json"], "staged pointer.json")
        catalog = _parse(expected["downloads.json"], "staged downloads.json")
    except Problem as e:
        return str(e)
    perr = pointer_schema_error(pointer)
    if perr:
        return "staged pointer invalid: " + perr
    cerr = catalog_schema_error(catalog)
    if cerr:
        return "staged catalog invalid: " + cerr
    err = pair_error(pointer, catalog)
    return ("staged " + err) if err else None


def load_expected(directory):
    """Capped read of the staged files. Raises StagedError (controlled)."""
    expected = {}
    for name, cap in FILES:
        path = os.path.join(directory, name)
        if not os.path.isfile(path):
            raise StagedError("staged %s missing from --expected dir" % name)
        with open(path, "rb") as f:
            data = f.read(cap + 1)
        if len(data) > cap:
            raise StagedError("staged %s exceeds its %d-byte cap" % (name, cap))
        expected[name] = data
    return expected


# ------------------------------------------------------------------ checks
def check_once(fetch, expected, budget, request_timeout=REQUEST_TIMEOUT_SECONDS):
    """One bounded fetch+compare cycle. budget() returns the seconds left.
    Returns (ok, detail). Served-side problems are retryable findings."""
    findings = []
    for name, cap in FILES:
        timeout = min(request_timeout, budget())
        if timeout < MIN_PROBE_SECONDS:
            findings.append("%s: deadline reached before the request" % name)
            break
        try:
            status, body, age = fetch(name, cap, timeout)
        except Problem as e:
            findings.append(str(e))
            continue
        except Exception as e:  # transport errors are retryable findings
            findings.append("%s: fetch error: %s: %s" % (name, type(e).__name__, e))
            continue
        if budget() <= 0:
            # Post-fetch check: bytes that arrived at or after the absolute
            # deadline are never accepted, however well-formed. Fail closed.
            findings.append("%s: response arrived after the absolute deadline; not accepted" % name)
            break
        if len(body) > cap:
            findings.append("%s: body exceeds %d-byte bound" % (name, cap))
            continue
        if status != 200:
            findings.append("%s: HTTP %s (Age=%s)" % (name, status, age))
            continue
        try:
            doc = _parse(body, name)
        except Problem as e:
            findings.append(str(e))
            continue
        serr = pointer_schema_error(doc) if name == "pointer.json" else catalog_schema_error(doc)
        if body == expected[name]:
            continue  # exact staged bytes served; staged schema already validated
        if serr:
            findings.append("%s: bytes differ from staged AND served document malformed: %s (Age=%s)" % (name, serr, age))
        else:
            findings.append(
                "%s: served is a DIFFERENT well-formed document "
                "(staged sha256 %s..., served %s..., Age=%s); "
                "possible stale edge, partial propagation, or a writer outside the pages lock - not accepted without provenance"
                % (name, _sha256(expected[name])[:12], _sha256(body)[:12], age))
    if findings:
        return False, "; ".join(findings)
    return True, "served pointer.json and downloads.json match the exact staged bytes"


def verify(fetch, expected, interval, deadline, clock=time.monotonic, sleep=time.sleep,
           request_timeout=REQUEST_TIMEOUT_SECONDS):
    """Absolute-deadline verification loop. Returns (exit_code, detail).
    deadline covers request time and sleeps, measured on `clock`."""
    staged_err = validate_staged(expected)
    if staged_err:
        return 2, staged_err
    start = clock()

    def budget():
        return deadline - (clock() - start)

    detail, attempt = "no check performed", 0
    while budget() >= MIN_PROBE_SECONDS:
        attempt += 1
        ok, detail = check_once(fetch, expected, budget, request_timeout)
        if ok:
            if budget() <= 0:
                # Final-success check: never certify served identity on
                # budget-expired time; a late success fails closed.
                detail = ("checks passed only after the absolute deadline expired; "
                          "a late success fails closed")
                break
            return 0, "attempt %d (%.0fs elapsed of %ss): %s" % (attempt, clock() - start, deadline, detail)
        left = budget()
        if left <= 0:
            break
        sleep(min(interval, left))
    return 1, "mismatch after %d attempts within the %ss absolute deadline: %s" % (attempt, deadline, detail)


def _hard_stop(signum=None, frame=None):
    print("::error::verifier hard stop reached (deadline + request timeout + grace); "
          "served identity NOT confirmed", file=sys.stderr)
    raise SystemExit(1)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-url", required=True, help="official Pages site URL (deploy step output)")
    ap.add_argument("--expected", required=True, help="dir with frozen staged pointer.json and downloads.json")
    ap.add_argument("--interval", type=int, default=60, help="seconds between checks (capped to remaining time)")
    ap.add_argument("--deadline", type=int, default=900, help="ABSOLUTE verification deadline in seconds (requests + sleeps)")
    a = ap.parse_args(argv)
    if not (1 <= a.interval <= a.deadline <= MAX_DEADLINE_SECONDS):
        print("abort: need 1 <= interval <= deadline <= %d" % MAX_DEADLINE_SECONDS, file=sys.stderr)
        return 2
    try:
        fetch = http_fetcher(a.base_url)
        expected = load_expected(a.expected)
    except ValueError as e:
        print("abort: %s" % e, file=sys.stderr)
        return 2
    except StagedError as e:
        print("abort: %s" % e, file=sys.stderr)
        return 2
    if hasattr(signal, "SIGALRM"):
        signal.signal(signal.SIGALRM, _hard_stop)
        signal.alarm(a.deadline + REQUEST_TIMEOUT_SECONDS + HARD_STOP_GRACE_SECONDS)
    code, detail = verify(fetch, expected, a.interval, a.deadline)
    stream = sys.stdout if code == 0 else sys.stderr
    print(detail, file=stream)
    if code == 1:
        print("::error::served identity did not converge to the staged bytes within the deadline; "
              "no automatic redeploy - owner investigation required", file=sys.stderr)
    return code


if __name__ == "__main__":
    sys.exit(main())
