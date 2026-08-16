#!/usr/bin/env python3
"""Blocking documentation checks (Phase 0 plan, Step 0; audit PR #13).

Roughly half of the 2026-08-04 governance review findings were mechanically
catchable. These checks catch those classes. Standard library only.

Checks:
1. link      — every relative Markdown link under docs/ resolves in-tree.
2. transience— living documents carry no session-state or deixis.
3. count     — living documents never numerically summarize a referenced list.
4. gatelog   — the roadmap gate-change log only grows at the bottom.
5. gate      — roadmap markers reserve phase-scoped implementation paths.

Exit 0 clean, 1 findings, 3 operational failure.
"""

from __future__ import annotations

import argparse
import json
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

PHASE_GATE_NAMESPACE = "docs-checks:phase-gate-"
PHASE_GATE_TAG = f"{PHASE_GATE_NAMESPACE}v1"
PHASE_GATE_LINE = re.compile(
    rf"^<!-- {re.escape(PHASE_GATE_TAG)} (?P<payload>.+) -->$"
)
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


def _invalid_gate(source, message):
    return None, [f"gate: {source} docs/roadmap.md: {message}"]


def _valid_gate_path(path):
    if not isinstance(path, str) or not path.endswith("/"):
        return False
    if (path.startswith(("/", "\\")) or
            re.match(r"^[A-Za-z]:/", path) or
            "\\" in path or any(char in path for char in "*?[")):
        return False
    parts = path[:-1].split("/")
    return bool(parts) and all(part not in ("", ".", "..") for part in parts)


def _phase_gate_slot(lines):
    try:
        index = lines.index("## Capability level") + 1
    except ValueError:
        return None
    while index < len(lines) and not lines[index].strip():
        index += 1
    table_start = index
    while index < len(lines) and lines[index].startswith("|"):
        index += 1
    if index == table_start:
        return None
    while index < len(lines) and not lines[index].strip():
        index += 1
    return index


def _parse_phase_gate(text, source):
    lines = text.splitlines()
    locations = [
        index for index, line in enumerate(lines)
        if PHASE_GATE_NAMESPACE in line
    ]
    if not locations:
        return None, []
    if len(locations) != 1:
        return _invalid_gate(source, "duplicate phase-gate markers")

    index = locations[0]
    if index != _phase_gate_slot(lines):
        return _invalid_gate(source, "invalid phase-gate marker placement")
    if not lines[index].startswith(f"<!-- {PHASE_GATE_TAG} "):
        return _invalid_gate(source, "unsupported phase-gate marker version")
    match = PHASE_GATE_LINE.fullmatch(lines[index])
    if match is None:
        return _invalid_gate(source, "invalid phase-gate marker syntax")
    def reject_duplicate_members(pairs):
        payload = {}
        for key, value in pairs:
            if key in payload:
                raise ValueError("duplicate JSON member")
            payload[key] = value
        return payload

    try:
        payload = json.loads(
            match.group("payload"), object_pairs_hook=reject_duplicate_members
        )
    except json.JSONDecodeError:
        return _invalid_gate(source, "invalid JSON in phase-gate marker")
    except ValueError:
        return _invalid_gate(source, "duplicate keys in phase-gate marker")

    if not isinstance(payload, dict) or set(payload) != {
            "phase", "state", "paths"}:
        return _invalid_gate(source, "invalid phase-gate marker schema")
    phase = payload["phase"]
    paths = payload["paths"]
    if type(phase) is not int or phase <= 0:
        return _invalid_gate(source, "phase must be a positive integer")
    if payload["state"] != "blocked" or type(payload["state"]) is not str:
        return _invalid_gate(source, "state must be 'blocked'")
    if (not isinstance(paths, list) or not paths or
            any(not _valid_gate_path(path) for path in paths) or
            len(set(paths)) != len(paths)):
        return _invalid_gate(source, "paths must be unique valid prefixes")
    return payload, []


def _resolve_gate_base(root, baseline_ref):
    try:
        proc = subprocess.run(
            ["git", "merge-base", baseline_ref, "HEAD"],
            cwd=root, capture_output=True, text=True, encoding="utf-8",
        )
    except (OSError, UnicodeDecodeError):
        return None
    if proc.returncode != 0:
        return None
    base = proc.stdout.strip()
    return base or None


def _gate_baseline_roadmap(root, base):
    try:
        proc = subprocess.run(
            ["git", "show", f"{base}:docs/roadmap.md"],
            cwd=root, capture_output=True, text=True, encoding="utf-8",
        )
    except (OSError, UnicodeDecodeError):
        return None
    return proc.stdout if proc.returncode == 0 else None


def _changed_paths(root, base):
    try:
        proc = subprocess.run(
            ["git", "diff", "--name-status", "-z", "--find-renames",
             f"{base}...HEAD"],
            cwd=root, capture_output=True, text=True, encoding="utf-8",
        )
    except (OSError, UnicodeDecodeError):
        return None
    if proc.returncode != 0:
        return None
    fields = proc.stdout.split("\0")
    paths = []
    index = 0
    while index < len(fields) and fields[index]:
        status = fields[index]
        index += 1
        count = 2 if status.startswith("R") else 1
        if index + count > len(fields):
            return None
        paths.extend(fields[index:index + count])
        index += count
    return list(dict.fromkeys(path for path in paths if path))


def check_gate(root, baseline_ref):
    base = _resolve_gate_base(root, baseline_ref)
    if base is None:
        return [f"gate: merge base could not be resolved from "
                f"{baseline_ref}; phase reservations cannot be evaluated"]

    baseline = _gate_baseline_roadmap(root, base)
    if baseline is None:
        return [f"gate: baseline roadmap could not be resolved from "
                f"{base}; phase reservations cannot be evaluated"]

    path = os.path.join(root, "docs", "roadmap.md")
    current = ""
    if os.path.exists(path):
        with open(path, encoding="utf-8") as fh:
            current = fh.read()

    baseline_gate, baseline_findings = _parse_phase_gate(
        baseline, "baseline"
    )
    current_gate, current_findings = _parse_phase_gate(current, "current")
    findings = baseline_findings + current_findings
    if findings:
        return findings

    reservations = {}
    for gate in (baseline_gate, current_gate):
        if gate is not None:
            reservations.setdefault(gate["phase"], set()).update(gate["paths"])
    if not reservations:
        return []

    changed = _changed_paths(root, base)
    if changed is None:
        return ["gate: changed paths could not be resolved; phase "
                "reservations cannot be evaluated"]

    blocked = {}
    for phase, prefixes in reservations.items():
        matches = sorted(
            path for path in changed
            if any(path.startswith(prefix) for prefix in prefixes)
        )
        if matches:
            blocked[phase] = matches
    return [
        f"gate: Phase {phase} reserved paths block changed files: "
        f"{', '.join(paths)}"
        for phase, paths in sorted(blocked.items())
    ]


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
