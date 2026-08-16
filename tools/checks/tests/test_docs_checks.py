"""Fixtures proving each docs check fails on its defect and passes clean."""

import os
import json
import shutil
import subprocess
import sys
import tempfile
import unittest

CHECKS_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, CHECKS_DIR)

import docs_checks  # noqa: E402


def git(args, cwd):
    subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True)


class DocsFixture(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="docs-checks-")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        os.makedirs(os.path.join(self.root, "docs"))

    def write(self, rel, content):
        path = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(content)


class TestLinks(DocsFixture):
    def test_broken_relative_link_fails(self):
        self.write("docs/a.md", "See [the plan](missing/plan.md).\n")
        findings = docs_checks.check_links(self.root)
        self.assertEqual(len(findings), 1)
        self.assertIn("does not resolve", findings[0])

    def test_resolving_link_and_urls_pass(self):
        self.write("docs/b.md", "# B\n")
        self.write("docs/a.md",
                   "See [b](b.md) and [site](https://example.com) "
                   "and [anchor](#local).\n")
        self.assertEqual(docs_checks.check_links(self.root), [])

    def test_code_span_links_ignored(self):
        self.write("docs/a.md", "Example: `[x](missing.md)` in code.\n")
        self.assertEqual(docs_checks.check_links(self.root), [])


class TestTransience(DocsFixture):
    def test_each_banned_phrase_fails_in_living_doc(self):
        cases = ["Recorded in This PR.", "The index is currently stale.",
                 "Fix is not yet merged.", "This needs a writer assignment.",
                 "Artifacts sit in the working tree."]
        for text in cases:
            self.write("docs/roadmap.md", text + "\n")
            findings = docs_checks.check_transience(self.root)
            self.assertTrue(findings, f"expected finding for: {text}")

    def test_dated_records_exempt(self):
        self.write("docs/reviews/audit.md",
                   "This PR currently sits in the working tree.\n")
        self.assertEqual(docs_checks.check_transience(self.root), [])

    def test_clean_living_doc_passes(self):
        self.write("docs/roadmap.md",
                   "Phase 0 is authorized (gate-change log, 2026-08-04).\n")
        self.assertEqual(docs_checks.check_transience(self.root), [])


class TestCounts(DocsFixture):
    def test_numeral_before_criteria_fails(self):
        for text in ("the ten conditions listed under acceptance",
                     "meets 12 criteria in the design"):
            self.write("docs/roadmap.md", text + "\n")
            self.assertTrue(docs_checks.check_counts(self.root), text)

    def test_reference_without_count_passes(self):
        self.write("docs/roadmap.md",
                   "Exit-gate criteria are the conditions listed in the "
                   "governing design.\n")
        self.assertEqual(docs_checks.check_counts(self.root), [])


class TestPhaseGateMarker(DocsFixture):
    ROADMAP = (
        "# Roadmap\n\n## Capability level\n\n"
        "| Phase | Capability | Status |\n"
        "|---|---|---|\n"
        "| 4 | Adapter | Authorized |\n\n"
        "Capability prose.\n\n"
        "## Gate-change log\n\n"
        "| Date | Decision | Recorded |\n|---|---|---|\n"
        "| 2026-08-02 | first | PR #1 |\n"
        "| 2026-08-03 | second | PR #2 |\n"
    )
    VALID = (
        '<!-- docs-checks:phase-gate-v1 '
        '{"phase":4,"state":"blocked","paths":["src/reserved/"]} -->'
    )

    def placed(self, marker):
        return self.ROADMAP.replace(
            "\nCapability prose.\n",
            f"\n{marker}\n\nCapability prose.\n",
            1,
        )

    def test_no_marker_returns_no_policy(self):
        self.assertEqual(
            docs_checks._parse_phase_gate(self.ROADMAP, "current"),
            (None, []),
        )

    def test_valid_marker_returns_policy(self):
        gate, findings = docs_checks._parse_phase_gate(
            self.placed(self.VALID), "current"
        )
        self.assertEqual(findings, [])
        self.assertEqual(gate, {
            "phase": 4,
            "state": "blocked",
            "paths": ["src/reserved/"],
        })

    def test_unsupported_marker_version_fails_closed(self):
        marker = self.VALID.replace("phase-gate-v1", "phase-gate-v2")
        gate, findings = docs_checks._parse_phase_gate(
            self.placed(marker), "current"
        )
        self.assertIsNone(gate)
        self.assertIn("unsupported", findings[0])

    def test_malformed_marker_fails_closed(self):
        marker = "<!-- docs-checks:phase-gate-v1 {not-json} -->"
        gate, findings = docs_checks._parse_phase_gate(
            self.placed(marker), "current"
        )
        self.assertIsNone(gate)
        self.assertIn("invalid JSON", findings[0])

    def test_duplicate_markers_fail_closed(self):
        gate, findings = docs_checks._parse_phase_gate(
            self.placed(f"{self.VALID}\n{self.VALID}"), "current"
        )
        self.assertIsNone(gate)
        self.assertIn("duplicate", findings[0])

    def test_schema_invalid_markers_fail_closed(self):
        valid = {"phase": 4, "state": "blocked", "paths": ["src/reserved/"]}
        payloads = [
            [],
            {"phase": 4, "state": "blocked"},
            {**valid, "extra": 1},
            {**valid, "phase": 0},
            {**valid, "phase": -1},
            {**valid, "phase": "4"},
            {**valid, "phase": True},
            {**valid, "state": "open"},
            {**valid, "state": 1},
            {**valid, "paths": "src/reserved/"},
            {**valid, "paths": []},
            {**valid, "paths": ["src/reserved/", "src/reserved/"]},
            {**valid, "paths": [1]},
        ]
        bad_paths = [
            "", "src/reserved", "src//reserved/", "src/./reserved/",
            "src/../reserved/", "src\\reserved/", "/src/reserved/",
            "C:/src/reserved/", "src/*/", "src/?/", "src/[/",
        ]
        payloads.extend({**valid, "paths": [path]} for path in bad_paths)

        for payload in payloads:
            marker = (
                "<!-- docs-checks:phase-gate-v1 "
                f"{json.dumps(payload, separators=(',', ':'))} -->"
            )
            with self.subTest(payload=payload):
                gate, findings = docs_checks._parse_phase_gate(
                    self.placed(marker), "current"
                )
                self.assertIsNone(gate)
                self.assertTrue(findings)

    def test_misplaced_markers_fail_closed(self):
        cases = {
            "before-table": self.ROADMAP.replace(
                "## Capability level\n\n",
                f"## Capability level\n\n{self.VALID}\n",
                1,
            ),
            "after-prose": self.ROADMAP.replace(
                "Capability prose.",
                f"Capability prose.\n\n{self.VALID}",
                1,
            ),
            "fenced-example": self.ROADMAP + f"\n```html\n{self.VALID}\n```\n",
            "gate-log": self.ROADMAP.replace(
                "## Gate-change log\n\n",
                f"## Gate-change log\n\n{self.VALID}\n",
                1,
            ),
        }
        for name, roadmap in cases.items():
            with self.subTest(name=name):
                gate, findings = docs_checks._parse_phase_gate(
                    roadmap, "current"
                )
                self.assertIsNone(gate)
                self.assertIn("placement", findings[0])


class GitDocsFixture(DocsFixture):
    """Fixtures needing a git history for baseline comparisons."""

    ROADMAP_V1 = (
        "# Roadmap\n\n## Gate-change log\n\n"
        "| Date | Decision | Recorded |\n|---|---|---|\n"
        "| 2026-08-02 | first | PR #1 |\n"
        "| 2026-08-03 | second | PR #2 |\n"
    )

    def setUp(self):
        super().setUp()
        git(["init", "-q", "-b", "main"], self.root)
        git(["config", "user.email", "t@t"], self.root)
        git(["config", "user.name", "t"], self.root)
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        git(["add", "."], self.root)
        git(["commit", "-q", "-m", "baseline"], self.root)
        self.baseline = "HEAD"

    def commit_branch(self, message="change"):
        git(["checkout", "-q", "-b", "feature"], self.root)
        git(["add", "-A", "."], self.root)
        git(["commit", "-q", "-m", message], self.root)
        self.baseline = "main"


class TestGatelog(GitDocsFixture):
    def test_appended_row_passes(self):
        self.write("docs/roadmap.md",
                   self.ROADMAP_V1 + "| 2026-08-04 | third | PR #3 |\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gatelog(self.root, self.baseline), [])

    def test_edited_row_fails(self):
        self.write("docs/roadmap.md",
                   self.ROADMAP_V1.replace("| 2026-08-02 | first |",
                                           "| 2026-08-02 | rewritten |"))
        self.commit_branch()
        self.assertTrue(docs_checks.check_gatelog(self.root, self.baseline))

    def test_reordered_rows_fail(self):
        reordered = self.ROADMAP_V1.replace(
            "| 2026-08-02 | first | PR #1 |\n| 2026-08-03 | second | PR #2 |",
            "| 2026-08-03 | second | PR #2 |\n| 2026-08-02 | first | PR #1 |")
        self.write("docs/roadmap.md", reordered)
        self.commit_branch()
        self.assertTrue(docs_checks.check_gatelog(self.root, self.baseline))


class TestGate(GitDocsFixture):
    MARKER = ("\nImplementation of the phase itself is gated separately and "
              "may not be claimed until the plan is approved.\n")

    def test_non_docs_diff_fails_while_gated(self):
        self.write("docs/roadmap.md", self.ROADMAP_V1 + self.MARKER)
        git(["add", "-A", "."], self.root)
        git(["commit", "-q", "-m", "add gate"], self.root)
        self.write("src/code.py", "print('impl')\n")
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertTrue(findings)
        self.assertIn("src/code.py", findings[0])

    def test_docs_only_diff_passes_while_gated(self):
        self.write("docs/roadmap.md", self.ROADMAP_V1 + self.MARKER)
        git(["add", "-A", "."], self.root)
        git(["commit", "-q", "-m", "add gate"], self.root)
        self.write("docs/other.md", "prose\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_gate_lifts_when_marker_removed_in_same_change(self):
        self.write("docs/roadmap.md", self.ROADMAP_V1 + self.MARKER)
        git(["add", "-A", "."], self.root)
        git(["commit", "-q", "-m", "add gate"], self.root)
        self.write("docs/roadmap.md",
                   self.ROADMAP_V1 + "| 2026-08-04 | authorized | PR #9 |\n")
        self.write("src/code.py", "print('impl')\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
