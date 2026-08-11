"""Self-check for the PR-review monitor.

Guards the regressions the plan names: a dropped surface, re-reporting an inline
comment after a push shifts its line, echoing our own comments, and swallowing a
gh failure silently. The fetch boundary is injected, so no live gh is needed;
two tests additionally pin the real gh_fetch contract via a stubbed subprocess.
"""

import json
import os
import sys
import unittest
from unittest import mock

TOOL_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, TOOL_DIR)

import watch_pr_reviews  # noqa: E402
from watch_pr_reviews import poll_once, PollFailure  # noqa: E402

REPO = "o/r"
PR = 7
SELF = "our-bot"


def _paths():
    return {s.kind: s.path_template.format(repo=REPO, n=PR)
            for s in watch_pr_reviews.SURFACES}


def _item(item_id, login="alice", body="hi", **extra):
    data = {"id": item_id, "user": {"login": login}, "body": body}
    data.update(extra)
    return data


def _fetch(by_kind, fail_kinds=()):
    path_to_kind = {path: kind for kind, path in _paths().items()}

    def fetch(path):
        kind = path_to_kind[path]
        if kind in fail_kinds:
            raise PollFailure("stub gh error")
        return list(by_kind.get(kind, []))

    return fetch


class FakeProc:
    def __init__(self, returncode, stdout="", stderr=""):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr


class PollOnceTests(unittest.TestCase):
    def test_reports_all_three_surfaces(self):
        # Missing-surface regression: dropping any surface fails this.
        fetch = _fetch({
            "review": [_item(1, body="", state="APPROVED")],
            "comment": [_item(2, body="inline", path="a.cs", line=10)],
            "issue-comment": [_item(3, body="conversation")],
        })
        events, failures = poll_once(REPO, PR, set(), SELF, fetch)
        self.assertEqual({e.kind for e in events},
                         {"review", "comment", "issue-comment"})
        self.assertEqual(failures, [])

    def test_empty_review_summarizes_to_state(self):
        fetch = _fetch({"review": [_item(1, body="", state="APPROVED")]})
        events, _ = poll_once(REPO, PR, set(), SELF, fetch)
        self.assertEqual(events[0].summary, "APPROVED")

    def test_dedups_across_polls(self):
        seen = set()
        fetch = _fetch({"issue-comment": [_item(3)]})
        first, _ = poll_once(REPO, PR, seen, SELF, fetch)
        second, _ = poll_once(REPO, PR, seen, SELF, fetch)
        self.assertEqual(len(first), 1)
        self.assertEqual(second, [])

    def test_inline_comment_survives_a_line_shift(self):
        # Keyed by (surface, id), not path:line — a push that moves the anchored
        # line must not re-report the same comment.
        seen = set()
        poll_once(REPO, PR, seen, SELF,
                  _fetch({"comment": [_item(2, path="a.cs", line=10)]}))
        shifted, _ = poll_once(REPO, PR, seen, SELF,
                               _fetch({"comment": [_item(2, path="a.cs", line=42)]}))
        self.assertEqual(shifted, [])

    def test_skips_our_own_identity(self):
        # Self-echo regression: our own comment must never be reported.
        fetch = _fetch({"issue-comment": [_item(3, login=SELF), _item(4, login="alice")]})
        events, _ = poll_once(REPO, PR, set(), SELF, fetch)
        self.assertEqual({e.id for e in events}, {4})

    def test_poll_fail_is_reported_and_does_not_abort_other_surfaces(self):
        # Silent-failure regression: a failing surface yields POLL-FAIL, and the
        # other surfaces are still read.
        fetch = _fetch(
            {"review": [_item(1)], "issue-comment": [_item(3)]},
            fail_kinds={"comment"},
        )
        events, failures = poll_once(REPO, PR, set(), SELF, fetch)
        self.assertEqual(len(failures), 1)
        self.assertIn("POLL-FAIL", failures[0])
        self.assertIn("comment", failures[0])
        self.assertEqual({e.kind for e in events}, {"review", "issue-comment"})


class GhFetchTests(unittest.TestCase):
    def test_nonzero_exit_raises_pollfailure(self):
        with mock.patch.object(watch_pr_reviews.subprocess, "run",
                               return_value=FakeProc(1, "", "boom")):
            with self.assertRaises(PollFailure):
                watch_pr_reviews.gh_fetch("repos/o/r/pulls/7/reviews")

    def test_flattens_paginated_pages(self):
        pages = json.dumps([[{"id": 1}], [{"id": 2}, {"id": 3}]])
        with mock.patch.object(watch_pr_reviews.subprocess, "run",
                               return_value=FakeProc(0, pages)):
            items = watch_pr_reviews.gh_fetch("repos/o/r/pulls/7/comments")
        self.assertEqual([i["id"] for i in items], [1, 2, 3])

    def test_missing_gh_raises_pollfailure(self):
        # gh absent raises FileNotFoundError (an OSError), not a non-zero exit;
        # it must surface as POLL-FAIL, not escape poll_once as a crash.
        with mock.patch.object(watch_pr_reviews.subprocess, "run",
                               side_effect=FileNotFoundError("gh")):
            with self.assertRaises(PollFailure):
                watch_pr_reviews.gh_fetch("repos/o/r/pulls/7/reviews")


class SelfIdentityGuardTests(unittest.TestCase):
    def _run_main(self, argv):
        import contextlib
        import io
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = watch_pr_reviews.main(argv)
        return rc, buf.getvalue()

    def test_refuses_when_identity_unresolved_and_no_self(self):
        # A failed `gh api user` must not silently leave the self-echo filter
        # inert; the monitor refuses instead.
        with mock.patch.object(watch_pr_reviews, "_gh_scalar", return_value=""):
            rc, out = self._run_main(["7", "--repo", "o/r", "--interval", "0"])
        self.assertEqual(rc, 1)
        self.assertIn("POLL-FAIL", out)
        self.assertIn("--self", out)

    def test_explicit_empty_self_is_allowed(self):
        # --self "" deliberately disables the filter and must be honored.
        with mock.patch.object(watch_pr_reviews, "gh_fetch", return_value=[]):
            rc, out = self._run_main(
                ["7", "--repo", "o/r", "--self", "", "--interval", "0"])
        self.assertEqual(rc, 0)
        self.assertNotIn("could not resolve the authenticated", out)


class BoundedSeenTests(unittest.TestCase):
    def test_evicts_oldest_beyond_maxsize(self):
        seen = watch_pr_reviews._BoundedSeen(maxsize=2)
        seen.add(("review", 1))
        seen.add(("comment", 2))
        seen.add(("issue-comment", 3))  # evicts the oldest, ("review", 1)
        self.assertNotIn(("review", 1), seen)
        self.assertIn(("comment", 2), seen)
        self.assertIn(("issue-comment", 3), seen)


if __name__ == "__main__":
    unittest.main()
