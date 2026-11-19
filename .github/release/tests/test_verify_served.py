#!/usr/bin/env python3
"""Offline tests for verify_served.py (v2). No network, no gh, no GitHub state:
the fetcher, clock and sleeper are injected. Fixtures follow the real
generator contract (pick_release_pointer.pointer_document and
catalog_projection.entry_for/project): the catalog entry has NO build field,
carries layout, and pointer/entry identities agree; sha256 and size are
nullable. Covers match, null selection, rollback, stale-then-match, absolute
deadline (slow requests, trickle bodies), URL allowlist, redirect refusal,
malformed/partial served bodies, and strict staged validation.
Run: python3 -m unittest discover -s .github/release/tests -v"""
import copy, json, os, sys, tempfile, unittest, urllib.error
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_served as vs  # noqa: E402

SHA = "a" * 40
BASE = "https://my-sochen.github.io/Start-app/"


def entry(tag="v0.1.0-proto.3", layout="portable-v1", sha="b" * 64, size=123):
    return {"schema": 1, "channel": "prototype", "label": "אב-טיפוס להערכה - לא גרסה יציבה",
            "version": tag[1:], "tag": tag, "commit": SHA, "asset": "StartApp-win-x64.zip",
            "url": "https://github.com/my-sochen/Start-app/releases/download/%s/StartApp-win-x64.zip" % tag,
            "sha256": sha, "size": size, "layout": layout}


def pointer(tag="v0.1.0-proto.3", **kw):
    d = entry(tag, **kw)
    d["generated_utc"] = "2026-11-19T00:00:00Z"
    d["scope"] = "Declared release metadata only; ZIP bytes and run evidence were not re-verified by this selector."
    return d


def null_pointer():
    return {"schema": 1, "channel": "prototype", "candidate": None,
            "reason": "no published prerelease with ui_gate=passed exists"}


def catalog(*entries):
    return {"schema": 1, "channel": "prototype", "complete": True,
            "selected_tag": entries[0]["tag"] if entries else None, "entries": list(entries)}


def enc(doc):
    return json.dumps(doc, ensure_ascii=False, indent=2).encode("utf-8")


def staged(pdoc=None, cdoc=None):
    pdoc = pdoc if pdoc is not None else pointer()
    cdoc = cdoc if cdoc is not None else catalog(entry())
    return {"pointer.json": enc(pdoc), "downloads.json": enc(cdoc)}


class Clock:
    """Fake monotonic clock advanced by sleeps and by simulated request time."""
    def __init__(self):
        self.t = 0.0

    def __call__(self):
        return self.t

    def sleep(self, s):
        self.t += s


def fake_fetch(responses, clock=None, latency=0.0, seen=None):
    state = {k: list(v) for k, v in responses.items()}

    def fetch(name, cap, timeout):
        if seen is not None:
            seen.append((name, timeout))
        if clock:
            clock.t += latency
        seq = state.setdefault(name, [(200, b"", None)])
        item = seq.pop(0) if len(seq) > 1 else seq[0]
        return item
    return fetch


def ok_pair(pdoc=None, cdoc=None):
    pdoc = pdoc if pdoc is not None else pointer()
    cdoc = cdoc if cdoc is not None else catalog(entry())
    return {"pointer.json": [(200, enc(pdoc), "0")], "downloads.json": [(200, enc(cdoc), "0")]}


def run_verify(fetcher, expected, deadline=100, interval=10, clock=None):
    clock = clock or Clock()
    return vs.verify(fetcher, expected, interval=interval, deadline=deadline, clock=clock, sleep=clock.sleep)


class VerifyTests(unittest.TestCase):
    def test_match_candidate(self):
        code, _ = run_verify(fake_fetch(ok_pair()), staged())
        self.assertEqual(code, 0)

    def test_match_null_selection(self):
        code, _ = run_verify(fake_fetch(ok_pair(null_pointer(), catalog())), staged(null_pointer(), catalog()))
        self.assertEqual(code, 0)

    def test_rollback_lower_build_matches_exact_bytes(self):
        old = "v0.1.0-proto.1"
        exp = staged(pointer(old), catalog(entry(old)))
        code, _ = run_verify(fake_fetch(ok_pair(pointer(old), catalog(entry(old)))), exp)
        self.assertEqual(code, 0)

    def test_stale_then_match_within_deadline(self):
        resp = ok_pair()
        resp["pointer.json"] = [(200, enc(pointer("v0.1.0-proto.2")), "612"), (200, enc(pointer()), "0")]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 0)
        self.assertIn("attempt 2", detail)

    def test_mismatch_deadline_fails_loudly(self):
        resp = ok_pair(pointer("v0.1.0-proto.9"))
        code, detail = run_verify(fake_fetch(resp), staged(), deadline=30, interval=10)
        self.assertEqual(code, 1)
        self.assertIn("DIFFERENT well-formed document", detail)
        self.assertIn("absolute deadline", detail)

    def test_malformed_served_fails_after_deadline(self):
        resp = ok_pair()
        resp["pointer.json"] = [(200, b"<html>error</html>", None)]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 1)
        self.assertIn("not valid UTF-8 JSON", detail)

    def test_partial_propagation_catalog_mismatch_fails(self):
        resp = ok_pair()
        resp["downloads.json"] = [(200, enc(catalog()), None)]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 1)
        self.assertIn("downloads.json", detail)

    def test_non_200_retried_then_fails(self):
        resp = ok_pair()
        resp["pointer.json"] = [(404, b"not found", None)]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 1)
        self.assertIn("HTTP 404", detail)

    def test_oversized_body_rejected(self):
        resp = ok_pair()
        resp["pointer.json"] = [(200, b" " * (vs.POINTER_MAX_BYTES + 1), None)]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 1)
        self.assertIn("bound", detail)

    def test_transport_error_is_retryable(self):
        def flaky(name, cap, timeout):
            if name == "pointer.json":
                raise OSError("connection reset")
            return (200, enc(catalog(entry())), None)
        code, detail = run_verify(flaky, staged())
        self.assertEqual(code, 1)
        self.assertIn("fetch error", detail)

    def test_served_schema_violation_reported(self):
        bad = pointer()
        bad["schema"] = True  # boolean is not integer 1
        resp = ok_pair()
        resp["pointer.json"] = [(200, enc(bad), None)]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 1)
        self.assertIn("malformed", detail)

    def test_catalog_envelope_enforced_on_served(self):
        badcat = catalog(entry())
        badcat["complete"] = False
        resp = ok_pair()
        resp["downloads.json"] = [(200, enc(badcat), None)]
        code, detail = run_verify(fake_fetch(resp), staged())
        self.assertEqual(code, 1)
        self.assertIn("complete", detail)


class DeadlineTests(unittest.TestCase):
    def test_slow_requests_cannot_exceed_absolute_deadline(self):
        # attempt-count semantics would allow many 40s requests; the absolute
        # deadline must stop the loop and never hand out more time than is left.
        clock, seen = Clock(), []
        resp = ok_pair(pointer("v0.1.0-proto.9"))
        code, detail = run_verify(fake_fetch(resp, clock, latency=40.0, seen=seen), staged(),
                                  deadline=100, interval=10, clock=clock)
        self.assertEqual(code, 1)
        self.assertTrue(all(t <= vs.REQUEST_TIMEOUT_SECONDS for _, t in seen))
        self.assertLessEqual(clock.t, 100 + 2 * 40)       # at most the in-flight request past the deadline
        self.assertLess(len(seen), 15)                     # far fewer than a count-based bound

    def test_request_timeout_is_capped_by_remaining_time(self):
        clock, seen = Clock(), []
        clock.t = 0
        code, _ = run_verify(fake_fetch(ok_pair(pointer("v0.1.0-proto.9")), clock, latency=0.0, seen=seen),
                             staged(), deadline=12, interval=10, clock=clock)
        self.assertEqual(code, 1)
        self.assertTrue(any(t < vs.REQUEST_TIMEOUT_SECONDS for _, t in seen))  # final probe shortened
        self.assertTrue(all(t >= vs.MIN_PROBE_SECONDS for _, t in seen))

    def test_sleep_is_capped_to_remaining(self):
        clock = Clock()
        slept = []
        orig = clock.sleep
        clock.sleep = lambda s: (slept.append(s), orig(s))
        vs.verify(fake_fetch(ok_pair(pointer("v0.1.0-proto.9"))), staged(), interval=60, deadline=25,
                  clock=clock, sleep=clock.sleep)
        self.assertTrue(slept)
        self.assertTrue(all(s <= 25 for s in slept))
        self.assertLessEqual(clock.t, 25)

    def test_total_elapsed_never_exceeds_deadline_with_instant_requests(self):
        clock = Clock()
        run_verify(fake_fetch(ok_pair(pointer("v0.1.0-proto.9"))), staged(), deadline=50, interval=7, clock=clock)
        self.assertLessEqual(clock.t, 50)

    def test_trickle_body_is_cut_by_wall_clock(self):
        clock = Clock()

        def trickle(n):          # one byte per read, each costing 1 s
            clock.t += 1.0
            return b"x"
        with self.assertRaises(vs.Problem):
            vs.read_capped(trickle, 10_000, deadline=5.0, clock=clock)

    def test_late_success_is_never_accepted(self):
        # Executor reproduction of the late-positive deadline defect: 100s
        # budget, two successful 60s fetches. v2 returned exit 0 at 120s;
        # the revision must fail closed and never accept budget-expired bytes.
        clock = Clock()
        code, detail = run_verify(fake_fetch(ok_pair(), clock=clock, latency=60.0), staged(),
                                  deadline=100, interval=10, clock=clock)
        self.assertEqual(code, 1)
        self.assertEqual(clock.t, 120)
        self.assertIn("after the absolute deadline", detail)

    def test_second_document_arriving_after_deadline_is_not_accepted(self):
        # pointer.json lands inside the budget; downloads.json ends past it.
        clock = Clock()
        code, detail = run_verify(fake_fetch(ok_pair(), clock=clock, latency=50.0), staged(),
                                  deadline=100, interval=10, clock=clock)
        self.assertEqual(code, 1)
        self.assertIn("downloads.json: response arrived after the absolute deadline", detail)

    def test_final_success_guard_fails_closed(self):
        # If time runs out between the last per-fetch check and the success
        # return, success is still refused.
        from unittest import mock
        clock = Clock()
        real = vs.check_once

        def wrapped(fetch, expected, budget, request_timeout=vs.REQUEST_TIMEOUT_SECONDS):
            out = real(fetch, expected, budget, request_timeout)
            clock.t += 1000.0            # budget expires after the in-loop checks
            return out

        with mock.patch.object(vs, "check_once", wrapped):
            code, detail = vs.verify(fake_fetch(ok_pair()), staged(), interval=10, deadline=100,
                                     clock=clock, sleep=clock.sleep)
        self.assertEqual(code, 1)
        self.assertIn("fails closed", detail)

    def test_eof_after_request_wall_bound_rejected(self):
        clock = Clock()
        chunks = iter([b"ab"])

        def read(n):
            clock.t += 4.0
            return next(chunks, b"")

        with self.assertRaises(vs.Problem) as cm:
            vs.read_capped(read, 100, deadline=5.0, clock=clock)
        self.assertIn("EOF", str(cm.exception))

    def test_blocking_read_past_wall_bound_rejected(self):
        clock = Clock()

        def read(n):
            clock.t += 30.0               # a read that blocked far past its bound
            return b"x"

        with self.assertRaises(vs.Problem) as cm:
            vs.read_capped(read, 100, deadline=5.0, clock=clock)
        self.assertIn("during a read", str(cm.exception))

    def test_blocked_read_is_capped_to_remaining_wall_by_set_timeout(self):
        # The transport hook must shrink the per-operation timeout to the
        # remaining request wall before EVERY read, so one blocking read
        # cannot outlast the request deadline.
        clock = Clock()
        installed = []

        def read(n):
            self.assertTrue(installed)             # timeout installed before any read
            clock.t += installed[-1]               # blocked read returns as it fires
            return b"x"

        with self.assertRaises(vs.Problem):
            vs.read_capped(read, 10_000, deadline=5.0, clock=clock,
                           set_timeout=installed.append)
        self.assertTrue(all(0 < s <= 5.0 for s in installed))

    def test_set_read_wall_reaches_socket_or_noops(self):
        class Sock:
            def __init__(self):
                self.t = None

            def settimeout(self, s):
                self.t = s

        class Raw:
            pass

        class FP:
            pass

        class Resp:
            pass

        sock, raw, fp, resp = Sock(), Raw(), FP(), Resp()
        raw._sock, fp.raw, resp.fp = sock, raw, fp
        self.assertTrue(vs._set_read_wall(resp, 3.0))
        self.assertEqual(sock.t, 3.0)
        self.assertFalse(vs._set_read_wall(object(), 3.0))   # unknown shape: no-op, no crash

    def test_read_capped_enforces_size_cap_and_returns_body(self):
        data = iter([b"ab", b"cd", b""])
        self.assertEqual(vs.read_capped(lambda n: next(data), 10, 1e9, lambda: 0.0), b"abcd")
        big = iter([b"a" * 6, b"a" * 6, b""])
        with self.assertRaises(vs.Problem):
            vs.read_capped(lambda n: next(big), 10, 1e9, lambda: 0.0)

    def test_hard_stop_exits_nonzero(self):
        with self.assertRaises(SystemExit) as cm:
            vs._hard_stop()
        self.assertEqual(cm.exception.code, 1)

    def test_main_rejects_bad_bounds(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(vs.main(["--base-url", BASE, "--expected", d, "--interval", "0"]), 2)
            self.assertEqual(vs.main(["--base-url", BASE, "--expected", d, "--deadline", "99999"]), 2)
            self.assertEqual(vs.main(["--base-url", BASE, "--expected", d, "--interval", "500", "--deadline", "100"]), 2)


class UrlPolicyTests(unittest.TestCase):
    GOOD = ["https://my-sochen.github.io/Start-app/", "https://my-sochen.github.io/Start-app",
            "https://MY-SOCHEN.github.io/Start-app/"]
    BAD = [
        "http://my-sochen.github.io/Start-app/",                     # not https
        "https://example.com/Start-app/",                            # arbitrary host
        "https://my-sochen.github.io.evil.com/Start-app/",           # suffix lookalike
        "https://evilmy-sochen.github.io/Start-app/",                # prefix lookalike
        "https://other.github.io/Start-app/",                        # other Pages owner
        "https://my-sochen.github.io./Start-app/",                   # trailing-dot host
        "https://user@my-sochen.github.io/Start-app/",               # userinfo
        "https://user:pw@my-sochen.github.io/Start-app/",
        "https://my-sochen.github.io@evil.com/Start-app/",           # userinfo disguised as host
        "https://my-sochen.github.io:8443/Start-app/",               # non-default port
        "https://my-sochen.github.io:443/Start-app/",                # explicit port refused too
        "https://my-sochen.github.io/Start-app/?x=1",                # query
        "https://my-sochen.github.io/Start-app/?",                   # empty query
        "https://my-sochen.github.io/Start-app/#frag",               # fragment
        "https://my-sochen.github.io/Start-app/#",                   # empty fragment
        "https://my-sochen.github.io/",                              # path drift: root
        "https://my-sochen.github.io",                               # no project path
        "https://my-sochen.github.io/Start-app/sub/",                # path drift: deeper
        "https://my-sochen.github.io/Start-app/../x/",               # traversal
        "https://my-sochen.github.io/Start-app//",                   # double slash
        "https://my-sochen.github.io/start-app/",                    # wrong case
        "https://my-sochen.github.io/Other/",                        # other project
        "https://my-sochen.github.io/Start-app;x=1/",                # path params
        "https://my-sochen.github.io\\@evil.com/Start-app/",         # backslash
        "https://my-sochen.github.io/Start-app/ ",                   # whitespace
        "https://my-sochen.github.io/Start-app/\n",
        "", None, "ftp://my-sochen.github.io/Start-app/",
    ]

    def test_good_urls_canonicalize(self):
        for u in self.GOOD:
            self.assertEqual(vs.validate_base_url(u), BASE, u)

    def test_bad_urls_rejected(self):
        for u in self.BAD:
            with self.assertRaises(ValueError, msg=repr(u)):
                vs.validate_base_url(u)

    def test_fetcher_refuses_bad_url_before_any_request(self):
        with self.assertRaises(ValueError):
            vs.http_fetcher("https://example.com/Start-app/")

    def test_main_exit_2_for_bad_url(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(vs.main(["--base-url", "https://example.com/", "--expected", d]), 2)

    def test_all_redirects_refused_including_same_host_path_change(self):
        h = vs._RefuseRedirects()
        req = urllib.request.Request(BASE + "pointer.json")
        for new in ("https://my-sochen.github.io/Start-app/pointer.json",       # same path
                    "https://my-sochen.github.io/other/pointer.json",           # same host, path drift
                    "https://evil.example/Start-app/pointer.json",              # cross host
                    "http://my-sochen.github.io/Start-app/pointer.json"):       # downgrade
            with self.assertRaises(urllib.error.HTTPError, msg=new):
                h.redirect_request(req, None, 301, "m", {}, new)


import urllib.request  # noqa: E402  (used by the redirect test)


class StagedValidationTests(unittest.TestCase):
    def mutate(self, fn_p=None, fn_c=None):
        p, c = pointer(), catalog(entry())
        if fn_p:
            fn_p(p)
        if fn_c:
            fn_c(c)
        return vs.validate_staged(staged(p, c))

    def assertStagedBad(self, err, fragment=None):
        self.assertIsNotNone(err)
        if fragment:
            self.assertIn(fragment, err)

    # --- both documents required, caps, controlled JSON errors
    def test_valid_staged_passes(self):
        self.assertIsNone(vs.validate_staged(staged()))

    def test_missing_catalog_is_staged_error(self):
        exp = staged()
        del exp["downloads.json"]
        self.assertStagedBad(vs.validate_staged(exp), "downloads.json is missing")
        self.assertEqual(run_verify(fake_fetch(ok_pair()), exp)[0], 2)

    def test_missing_pointer_is_staged_error(self):
        exp = staged()
        del exp["pointer.json"]
        self.assertEqual(run_verify(fake_fetch(ok_pair()), exp)[0], 2)

    def test_malformed_staged_json_is_controlled_exit_2_not_traceback(self):
        for bad in (b"{not json", b"\xff\xfe", b"", b"[1,2", b"NaN", b'{"a":1,"a":2}', b"[" * 100000):
            for name in ("pointer.json", "downloads.json"):
                exp = staged()
                exp[name] = bad
                code, detail = run_verify(fake_fetch(ok_pair()), exp)
                self.assertEqual(code, 2, (name, bad[:10]))
                self.assertIn("not valid UTF-8 JSON", detail)

    def test_staged_over_cap_rejected(self):
        exp = staged()
        exp["pointer.json"] = b" " * (vs.POINTER_MAX_BYTES + 1)
        self.assertStagedBad(vs.validate_staged(exp), "cap")
        exp = staged()
        exp["downloads.json"] = b" " * (vs.CATALOG_MAX_BYTES + 1)
        self.assertStagedBad(vs.validate_staged(exp), "cap")

    def test_load_expected_capped_and_requires_both_files(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "pointer.json").write_bytes(enc(pointer()))
            with self.assertRaises(vs.StagedError):
                vs.load_expected(d)                      # catalog missing
            (Path(d) / "downloads.json").write_bytes(b" " * (vs.CATALOG_MAX_BYTES + 1))
            with self.assertRaises(vs.StagedError):
                vs.load_expected(d)                      # over cap
            (Path(d) / "downloads.json").write_bytes(enc(catalog(entry())))
            self.assertEqual(set(vs.load_expected(d)), {"pointer.json", "downloads.json"})

    def test_main_exit_2_when_staged_catalog_missing(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "pointer.json").write_bytes(enc(pointer()))
            self.assertEqual(vs.main(["--base-url", BASE, "--expected", d]), 2)

    # --- typed fields
    def test_boolean_is_not_schema_one(self):
        self.assertStagedBad(self.mutate(lambda p: p.update(schema=True)), "schema")
        self.assertStagedBad(self.mutate(None, lambda c: c.update(schema=True)), "schema")
        self.assertStagedBad(self.mutate(None, lambda c: c["entries"][0].update(schema=True)), "schema")
        self.assertStagedBad(self.mutate(lambda p: p.update(schema=1.0)), "schema")

    def test_channel_and_complete_typed(self):
        self.assertStagedBad(self.mutate(lambda p: p.update(channel="stable")), "channel")
        self.assertStagedBad(self.mutate(None, lambda c: c.update(complete=1)), "complete")
        self.assertStagedBad(self.mutate(None, lambda c: c.update(complete="true")), "complete")

    def test_size_typing(self):
        for bad in (True, 1.5, "123", 0, -1, vs.SAFE_INT + 1, [1]):
            self.assertStagedBad(self.mutate(lambda p, b=bad: p.update(size=b)), "size")
            self.assertStagedBad(self.mutate(None, lambda c, b=bad: c["entries"][0].update(size=b)), "size")

    def test_digest_typing(self):
        for bad in ("B" * 64, "b" * 63, "b" * 65, 123, "", "g" * 64):
            self.assertStagedBad(self.mutate(lambda p, b=bad: p.update(sha256=b)), "sha256")

    def test_commit_typing(self):
        for bad in ("a" * 39, "A" * 40, None, 5):
            self.assertStagedBad(self.mutate(lambda p, b=bad: p.update(commit=b)), "commit")

    def test_tag_version_asset_url_layout_typing(self):
        self.assertStagedBad(self.mutate(lambda p: p.update(tag="v1.0.0")), "tag")
        self.assertStagedBad(self.mutate(lambda p: p.update(version="9.9.9-proto.9")), "version")
        self.assertStagedBad(self.mutate(lambda p: p.update(asset="Other.zip")), "asset")
        self.assertStagedBad(self.mutate(lambda p: p.update(url="https://evil.example/x.zip")), "url")
        self.assertStagedBad(self.mutate(lambda p: p.update(url=p["url"].replace("proto.3", "proto.2"))), "url")
        self.assertStagedBad(self.mutate(lambda p: p.update(url=p["url"].replace("my-sochen", "other"))), "url")
        self.assertStagedBad(self.mutate(lambda p: p.update(layout="flat")), "layout")
        self.assertStagedBad(self.mutate(lambda p: p.update(layout=None)), "layout")
        self.assertStagedBad(self.mutate(lambda p: p.pop("layout")), "layout")
        self.assertStagedBad(self.mutate(lambda p: p.update(generated_utc="yesterday")), "generated_utc")
        self.assertStagedBad(self.mutate(lambda p: p.pop("scope")), "scope")
        self.assertStagedBad(self.mutate(lambda p: p.update(label=5)), "label")

    def test_legacy_layout_only_for_legacy_pin(self):
        self.assertStagedBad(self.mutate(lambda p: p.update(layout="legacy-flat-v1"),
                                         lambda c: c["entries"][0].update(layout="legacy-flat-v1")), "legacy")

    def test_null_pointer_typing(self):
        for p in ({"schema": 1, "channel": "prototype", "candidate": None},
                  {"schema": 1, "channel": "prototype", "candidate": None, "reason": 5},
                  {"schema": 1, "channel": "prototype", "candidate": None, "reason": ""},
                  {"schema": 1, "channel": "prototype", "candidate": {}, "reason": "x"}):
            self.assertStagedBad(vs.validate_staged(staged(p, catalog())))

    # --- catalog structure
    def test_catalog_structure_errors(self):
        self.assertStagedBad(self.mutate(None, lambda c: c.update(entries={})), "entries")
        self.assertStagedBad(self.mutate(None, lambda c: c.pop("selected_tag")), "selected_tag")
        self.assertStagedBad(self.mutate(None, lambda c: c.update(selected_tag="bogus")), "selected_tag")
        self.assertStagedBad(self.mutate(None, lambda c: c["entries"].append("x")), "entry")
        self.assertStagedBad(self.mutate(None, lambda c: c["entries"][0].pop("layout")), "layout")

    def test_catalog_order_duplicates_and_selected_first(self):
        e3, e2, e1 = entry("v0.1.0-proto.3"), entry("v0.1.0-proto.2"), entry("v0.1.0-proto.1")
        self.assertIsNone(vs.validate_staged(staged(pointer(), catalog(e3, e2, e1))))
        self.assertStagedBad(vs.validate_staged(staged(pointer(), catalog(e3, e1, e2))), "ordered")
        self.assertStagedBad(vs.validate_staged(staged(pointer(), catalog(e3, copy.deepcopy(e3)))), "duplicate")
        # same build number on two version lines is ambiguous in the generator
        self.assertStagedBad(vs.validate_staged(staged(pointer(), catalog(e3, entry("v0.2.0-proto.3")))), "duplicate")

    # --- pointer/catalog consistency
    def test_selected_tag_vs_pointer_tag(self):
        c = catalog(entry("v0.1.0-proto.2"))
        self.assertStagedBad(vs.validate_staged(staged(pointer(), c)), "selected_tag")

    def test_pointer_vs_entry_identity_fields(self):
        for k, v in (("sha256", "c" * 64), ("size", 999), ("commit", "d" * 40), ("layout", "unknown"),
                     ("label", "other")):
            self.assertStagedBad(self.mutate(None, lambda c, k=k, v=v: c["entries"][0].update({k: v})), k)

    def test_null_pointer_vs_nonempty_catalog(self):
        self.assertStagedBad(vs.validate_staged(staged(null_pointer(), catalog(entry()))), "null pointer")

    def test_selected_pointer_vs_empty_catalog(self):
        self.assertStagedBad(vs.validate_staged(staged(pointer(), catalog())), "selected_tag")

    # --- legitimate nullable metadata and layout contract preserved
    def test_nullable_sha_and_size_accepted(self):
        p = pointer(sha=None, size=None)
        c = catalog(entry(sha=None, size=None))
        self.assertIsNone(vs.validate_staged(staged(p, c)))

    def test_unknown_layout_accepted(self):
        self.assertIsNone(vs.validate_staged(staged(pointer(layout="unknown"), catalog(entry(layout="unknown")))))

    def test_legacy_pin_with_each_legal_layout(self):
        tag, commit, sha, size = vs.LEGACY
        for layout in ("legacy-flat-v1", "unknown", "portable-v1"):
            e = entry(tag, layout, sha, size)
            e["commit"] = commit
            p = dict(e)
            p["generated_utc"] = "2026-11-19T00:00:00Z"
            p["scope"] = "s"
            self.assertIsNone(vs.validate_staged(staged(p, catalog(e))), layout)

    def test_extra_unknown_fields_tolerated(self):
        p = pointer()
        p["future_field"] = {"x": 1}
        self.assertIsNone(vs.validate_staged(staged(p, catalog(entry()))))

    def test_staged_corruption_fails_fast_through_verify(self):
        exp = staged()
        exp["pointer.json"] = enc({"schema": 2})
        code, detail = run_verify(fake_fetch(ok_pair()), exp)
        self.assertEqual(code, 2)
        self.assertIn("staged pointer invalid", detail)


class GeneratorDriftTests(unittest.TestCase):
    """Runs only where the pinned generator modules are importable (the real
    repo). Skipped when they are not on the path."""

    @classmethod
    def setUpClass(cls):
        try:
            import catalog_projection as cp
            import pick_release_pointer as sel
        except Exception:
            raise unittest.SkipTest("generator modules not importable here")
        cls.cp, cls.sel = cp, sel

    def test_constants_match_generator(self):
        cp, sel = self.cp, self.sel
        self.assertEqual((vs.REPO, vs.ASSET, vs.SAFE_INT, vs.MAX_RELEASES, vs.LEGACY, vs.IDENTITY),
                         (cp.REPO, sel.ASSET, cp.SAFE_INT, cp.MAX_RELEASES, cp.LEGACY, cp.IDENTITY))
        self.assertEqual((vs.TAG_RE.pattern, vs.SHA_RE.pattern), (sel.TAG_RE.pattern, sel.SHA_RE.pattern))
        self.assertEqual((vs.POINTER_MAX_BYTES, vs.CATALOG_MAX_BYTES), (cp.MAX_JSON_BYTES, cp.MAX_CATALOG_BYTES))

    def test_real_generator_output_validates(self):
        cp, sel = self.cp, self.sel
        def rel(n):
            tag = "v0.1.0-proto.%d" % n
            return {"tag_name": tag, "draft": False, "prerelease": True,
                    "assets": [{"name": sel.ASSET, "id": 1, "browser_download_url":
                                "https://github.com/%s/releases/download/%s/%s" % (cp.REPO, tag, sel.ASSET)},
                               {"name": "manifest.json", "id": 2}]}
        def man(n):
            return {"channel": "prototype", "prerelease": True, "version": "0.1.0-proto.%d" % n,
                    "commit": "a" * 40, "unit_tests": "passed", "ui_gate": "passed", "asset": sel.ASSET,
                    "sha256": "b" * 64, "size": 10, "layout": "portable-v1"}
        rels = [rel(1), rel(2)]
        mans = {r["tag_name"]: man(int(r["tag_name"][-1])) for r in rels}
        p, c = cp.project(rels, mans)
        self.assertIsNone(vs.validate_staged({"pointer.json": enc(p), "downloads.json": enc(c)}))
        p, c = cp.project([], {})
        self.assertIsNone(vs.validate_staged({"pointer.json": enc(p), "downloads.json": enc(c)}))


if __name__ == "__main__":
    unittest.main()
