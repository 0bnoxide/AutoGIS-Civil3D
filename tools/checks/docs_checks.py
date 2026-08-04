#!/usr/bin/env python3
"""Blocking documentation checks (Phase 0 plan, Step 0; audit PR #13).

Roughly half of the 2026-08-04 governance review findings were mechanically
catchable. These checks catch those classes. Standard library only.

Checks:
1. link      — every relative Markdown link under docs/ resolves in-tree.
2. transience— living documents carry no session-state or deixis.
3. count     — living documents never numerically summarize a referenced list.
4. gatelog   — the roadmap gate-change log only grows at the bottom.
5. gate      — while the roadmap says Phase 0 implementation is unclaimable,
               a change may not leave docs/ (the paper gate, made real).

Exit 0 clean, 1 findings, 3 operational failure.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

# The durable-authoritative set: files that must never carry point-in-time
# state. Dated records (ADRs, reviews, specs, plans) legitimately speak from
# their date and are exempt from transience linting.
LIVING_DOCS = (
    "docs/roadmap.md",
    "docs/agent-guide.md",
    "docs/collaboration.md",
    "README.md",
    "CONTRIBUTING.md",
)

TRANSIENCE_PATTERNS = (
    (re.compile(r"\bthis PR\b", re.IGNORECASE), "self-reference 'This PR'"),
    (re.compile(r"\bcurrently\b", re.IGNORECASE), "'currently'"),
    (re.compile(r"\bright now\b", re.IGNORECASE), "'right now'"),
    (re.compile(r"\bas of today\b", re.IGNORECASE), "'as of today'"),
    (re.compile(r"\bat the time of writing\b", re.IGNORECASE),
     "'at the time of writing'"),
    (re.compile(r"\bnot yet merged\b", re.IGNORECASE), "'not yet merged'"),
    (re.compile(r"\bneeds a writer\b", re.IGNORECASE), "actor assignment"),
    (re.compile(r"\bsit(?:s|ting)? in the working tree\b", re.IGNORECASE),
     "working-tree state assertion"),
)

# Negative lookbehind: "Phase 0 acceptance criteria" is a section name, not
# a count of one.
COUNT_PATTERN = re.compile(
    r"(?<![Pp]hase )\b(?:one|two|three|four|five|six|seven|eight|nine|ten|"
    r"eleven|twelve|\d+)\s+(?:acceptance\s+)?(?:conditions|criteria)\b",
    re.IGNORECASE,
)

GATE_MARKER = "may not be claimed until"
LINK_RE = re.compile(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)]*)?\)")
CODE_SPAN_RE = re.compile(r"`[^`]*`")
FENCE_RE = re.compile(r"^(```|~~~)")


def repo_files(root, subdir="docs"):
    for dirpath, _dirnames, filenames in os.walk(os.path.join(root, subdir)):
        for name in filenames:
            if name.lower().endswith(".md"):
                yield os.path.join(dirpath, name)


def strip_code(lines):
    """Yield (lineno, text) with fenced blocks and inline code removed —
    quoted examples inside code spans are not lint findings."""
    in_fence = False
    for lineno, line in enumerate(lines, start=1):
        if FENCE_RE.match(line.strip()):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        yield lineno, CODE_SPAN_RE.sub("", line)


def check_links(root):
    findings = []
    for path in repo_files(root):
        base = os.path.dirname(path)
        with open(path, encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        for lineno, text in strip_code(lines):
            for match in LINK_RE.finditer(text):
                target = match.group(1)
                if re.match(r"^[a-z]+:", target):  # http:, https:, mailto:
                    continue
                resolved = os.path.normpath(os.path.join(base, target))
                if not os.path.exists(resolved):
                    rel = os.path.relpath(path, root)
                    findings.append(
                        f"link: {rel}:{lineno}: '{target}' does not resolve"
                    )
    return findings


def check_transience(root):
    findings = []
    for rel in LIVING_DOCS:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        for lineno, text in strip_code(lines):
            for pattern, label in TRANSIENCE_PATTERNS:
                if pattern.search(text):
                    findings.append(
                        f"transience: {rel}:{lineno}: {label} — living "
                        "documents may not assert point-in-time state"
                    )
    return findings


def check_counts(root):
    findings = []
    for rel in LIVING_DOCS:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        for lineno, text in strip_code(lines):
            if COUNT_PATTERN.search(text):
                findings.append(
                    f"count: {rel}:{lineno}: numeral summarizing a criteria "
                    "list — reference the list, never count it"
                )
    return findings


def _gatelog_rows(text):
    rows, in_log = [], False
    for line in text.splitlines():
        if line.startswith("## "):
            in_log = line.lower().startswith("## gate-change log")
            continue
        if in_log and re.match(r"^\|\s*\d{4}-\d{2}-\d{2}\s*\|", line):
            rows.append(line.strip())
    return rows


def _resolve_baseline(root, baseline_ref):
    """Compare against merge-base(HEAD, ref): a branch that predates newer
    commits on the baseline must not false-fail append-only checks."""
    proc = subprocess.run(
        ["git", "merge-base", "HEAD", baseline_ref],
        cwd=root, capture_output=True, text=True, encoding='utf-8',
    )
    if proc.returncode == 0 and proc.stdout.strip():
        return proc.stdout.strip()
    return baseline_ref


def _baseline_roadmap(root, baseline_ref):
    base = _resolve_baseline(root, baseline_ref)
    proc = subprocess.run(
        ["git", "show", f"{base}:docs/roadmap.md"],
        cwd=root, capture_output=True, text=True, encoding='utf-8',
    )
    return proc.stdout if proc.returncode == 0 else None


def check_gatelog(root, baseline_ref):
    path = os.path.join(root, "docs", "roadmap.md")
    if not os.path.exists(path):
        return []
    baseline = _baseline_roadmap(root, baseline_ref)
    if baseline is None:
        return []  # no baseline (fresh history): nothing to compare
    with open(path, encoding="utf-8") as fh:
        current = fh.read()
    old_rows = _gatelog_rows(baseline)
    new_rows = _gatelog_rows(current)
    if new_rows[: len(old_rows)] != old_rows:
        return ["gatelog: docs/roadmap.md: gate-change log rows were edited "
                "or reordered — the log is append-only; corrections are new "
                "rows"]
    return []


def check_gate(root, baseline_ref):
    path = os.path.join(root, "docs", "roadmap.md")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as fh:
        roadmap = fh.read()
    if GATE_MARKER not in roadmap:
        return []
    base = _resolve_baseline(root, baseline_ref)
    proc = subprocess.run(
        ["git", "diff", "--name-only", f"{base}...HEAD"],
        cwd=root, capture_output=True, text=True, encoding='utf-8',
    )
    if proc.returncode != 0:
        return []  # no baseline: gate not evaluable here
    outside = [
        f for f in proc.stdout.splitlines()
        if f and not f.startswith("docs/")
    ]
    if outside:
        listed = ", ".join(outside[:5])
        return [f"gate: docs/roadmap.md states Phase 0 implementation is "
                f"unclaimable, but this change touches non-docs paths: "
                f"{listed}. Record the authorization in the roadmap first"]
    return []


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--baseline", default="origin/main",
                        help="ref for append-only and gate comparisons")
    parser.add_argument("--skip-gate", action="store_true",
                        help="for post-merge contexts (push to main)")
    args = parser.parse_args(argv)
    root = os.path.abspath(args.root)
    if not os.path.isdir(os.path.join(root, "docs")):
        print("docs_checks: no docs/ directory under --root", file=sys.stderr)
        return 3

    findings = []
    findings += check_links(root)
    findings += check_transience(root)
    findings += check_counts(root)
    findings += check_gatelog(root, args.baseline)
    if not args.skip_gate:
        findings += check_gate(root, args.baseline)

    if findings:
        print("docs_checks: FAIL")
        for finding in findings:
            print(f"  - {finding}")
        return 1
    print("docs_checks: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
