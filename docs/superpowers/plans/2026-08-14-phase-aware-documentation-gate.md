# Phase-Aware Documentation Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the retired Phase 0 phrase-triggered global documentation gate with the approved strict, phase-scoped roadmap marker from issue #89.

**Architecture:** Keep the existing `check_gate(root, baseline_ref)` seam and standard-library-only checker. First add a small parser that validates the one allowed marker slot and payload; then make `check_gate()` resolve a strict gate-specific merge base, parse that baseline roadmap and the current roadmap, union their reservations, and match both endpoints from a NUL-delimited rename-aware Git diff.

**Tech Stack:** Python 3 standard library (`json`, `re`, `subprocess`, `unittest`), Git, existing repository documentation checks, .NET 8 validation suite.

**Design:** [`2026-08-14-phase-aware-documentation-gate-design.md`](../specs/2026-08-14-phase-aware-documentation-gate-design.md)

## Global Constraints

- Change only `tools/checks/docs_checks.py` and `tools/checks/tests/test_docs_checks.py`.
- Keep `check_gate(root, baseline_ref)`, `main(argv=None)`, `--baseline`, and `--skip-gate` as the public command surface.
- Add no dependency, workflow, GitHub label, pull-request template, roadmap marker, or Phase 4 implementation path.
- The marker is exactly one single-line HTML comment whose JSON object has exactly `phase`, `state`, and `paths`.
- `phase` is a positive integer but not a boolean; `state` is exactly `blocked`; `paths` is a non-empty unique list of repository-relative directory prefixes.
- Every prefix uses `/`, ends in `/`, and rejects empty, `.`, or `..` segments, backslashes, Unix or Windows absolute prefixes, and `*`, `?`, or `[` glob metacharacters.
- A marker is valid only as the first nonblank line after the `## Capability level` table and before its explanatory prose.
- Evaluate both merge-base and current roadmap markers and enforce the union of their phase/path reservations.
- Collect changed names with status-aware, NUL-delimited rename detection and evaluate both source and destination of every rename.
- Fail closed on malformed, duplicate, schema-invalid, misplaced, or unsupported-version markers and when the merge base, baseline roadmap, or changed-path diff cannot be resolved.
- Keep the existing lenient `_resolve_baseline()` and `_baseline_roadmap()` semantics used by the gate-change log; the phase gate gets separate strict helpers.
- The legacy literal `may not be claimed until` has no gate behavior after this change.

---

## File Map

- `tools/checks/docs_checks.py`: Owns marker recognition, marker validation, baseline/current reservation union, Git changed-path decoding, and phase-specific findings.
- `tools/checks/tests/test_docs_checks.py`: Owns all marker-contract, lifecycle, bypass, rename, legacy-retirement, and fail-closed regressions using temporary Git repositories.

### Task 1: Parse and validate the roadmap marker

**Files:**
- Modify: `tools/checks/docs_checks.py:1-60` and immediately before `check_gate()`
- Modify: `tools/checks/tests/test_docs_checks.py:1-16`, `GitDocsFixture`, and the gate-test section

**Interfaces:**
- Consumes: roadmap text as `str` and a diagnostic source label as `str`.
- Produces: `_parse_phase_gate(text, source) -> tuple[dict | None, list[str]]`; the dict is the validated JSON object and findings are already prefixed with `gate:`.

- [ ] **Step 1: Add failing marker-contract tests**

Add `import json` and place the following focused parser-test class immediately before `GitDocsFixture`, so Task 2 can reuse its realistic roadmap text. Keep the existing link, transience, count, and gate-log tests unchanged.

```python
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
```

- [ ] **Step 2: Run the marker tests to verify RED**

Run:

```powershell
python tools/checks/tests/test_docs_checks.py TestPhaseGateMarker -v
```

Expected: FAIL because `docs_checks._parse_phase_gate` does not exist.

- [ ] **Step 3: Implement the minimal strict parser**

Add `import json`, the broader namespace detector, the supported version tag, and the exact-line expression beside the existing constants. Keep the retired `GATE_MARKER` constant until Task 2 so the intermediate commit leaves the existing `check_gate()` runnable. Add these helpers before `check_gate()`:

```python
PHASE_GATE_NAMESPACE = "docs-checks:phase-gate-"
PHASE_GATE_TAG = f"{PHASE_GATE_NAMESPACE}v1"
PHASE_GATE_LINE = re.compile(
    rf"^<!-- {re.escape(PHASE_GATE_TAG)} (?P<payload>.+) -->$"
)


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
    try:
        payload = json.loads(match.group("payload"))
    except json.JSONDecodeError:
        return _invalid_gate(source, "invalid JSON in phase-gate marker")

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
```

- [ ] **Step 4: Run parser and complete existing tests to verify GREEN**

Run:

```powershell
python tools/checks/tests/test_docs_checks.py TestPhaseGateMarker -v
python -m unittest discover -s tools/checks/tests -p "test_docs_checks.py" -v
```

Expected: all parser tests and all pre-existing documentation-check tests PASS.

- [ ] **Step 5: Commit the parser contract**

```powershell
git add tools/checks/docs_checks.py tools/checks/tests/test_docs_checks.py
git commit -m "feat: validate phase gate markers (#89)"
```

### Task 2: Enforce baseline/current reservations and rename endpoints

**Files:**
- Modify: `tools/checks/docs_checks.py:1-15` and the existing `check_gate()` block
- Modify: `tools/checks/tests/test_docs_checks.py` gate fixtures and tests

**Interfaces:**
- Consumes: `_parse_phase_gate(text, source)` from Task 1. The existing lenient baseline helpers remain unchanged for the gate-change log.
- Produces: `_resolve_gate_base(root, baseline_ref) -> str | None`, `_gate_baseline_roadmap(root, base) -> str | None`, `_changed_paths(root, base) -> list[str] | None`, and a phase-aware `check_gate(root, baseline_ref) -> list[str]`; every `None` is a gate-resolution failure and must become a blocking finding.

- [ ] **Step 1: Replace the retired global-gate tests with lifecycle and bypass regressions**

Add `from unittest import mock`, extend `GitDocsFixture` with small commit helpers, and use the same canonical marker slot as Task 1:

```python
class GitDocsFixture(DocsFixture):
    ROADMAP_V1 = TestPhaseGateMarker.ROADMAP
    MARKER = TestPhaseGateMarker.VALID

    def setUp(self):
        super().setUp()
        git(["init", "-q", "-b", "main"], self.root)
        git(["config", "user.email", "t@t"], self.root)
        git(["config", "user.name", "t"], self.root)
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        git(["add", "."], self.root)
        git(["commit", "-q", "-m", "baseline"], self.root)
        self.baseline = "HEAD"

    def roadmap_with_marker(self, marker=None, roadmap=None):
        marker = marker or self.MARKER
        roadmap = roadmap or self.ROADMAP_V1
        return roadmap.replace(
            "\nCapability prose.\n",
            f"\n{marker}\n\nCapability prose.\n",
            1,
        )

    def commit(self, message):
        git(["add", "-A", "."], self.root)
        git(["commit", "-q", "-m", message], self.root)

    def start_branch(self):
        git(["checkout", "-q", "-b", "feature"], self.root)
        self.baseline = "main"

    def commit_branch(self, message="change"):
        self.start_branch()
        self.commit(message)
```

Replace the old `TestGate` methods with these exact behaviors:

```python
class TestGate(GitDocsFixture):
    def activate(self, marker=None):
        self.write(
            "docs/roadmap.md",
            self.roadmap_with_marker(marker=marker),
        )
        self.commit("activate gate")

    def test_no_marker_allows_unrelated_change(self):
        self.write("src/maintenance.py", "pass\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_legacy_phrase_does_not_activate_gate(self):
        self.write(
            "docs/roadmap.md",
            self.ROADMAP_V1 + "\nmay not be claimed until a plan is approved.\n",
        )
        self.write("src/maintenance.py", "pass\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_docs_only_marker_addition_passes(self):
        self.write("docs/roadmap.md", self.roadmap_with_marker())
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_marker_addition_with_reserved_change_fails(self):
        self.write("docs/roadmap.md", self.roadmap_with_marker())
        self.write("src/reserved/new.py", "pass\n")
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("Phase 4", findings[0])
        self.assertIn("src/reserved/new.py", findings[0])

    def test_active_marker_blocks_reserved_change(self):
        self.activate()
        self.write("src/reserved/code.py", "pass\n")
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("Phase 4", findings[0])
        self.assertIn("src/reserved/code.py", findings[0])

    def test_active_marker_allows_unlisted_non_docs_change(self):
        self.activate()
        self.write("src/reserved-other/code.py", "pass\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_docs_only_marker_removal_passes(self):
        self.activate()
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_marker_removal_with_reserved_change_fails(self):
        self.activate()
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        self.write("src/reserved/code.py", "pass\n")
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("src/reserved/code.py", findings[0])

    def test_rename_source_under_reserved_prefix_fails(self):
        self.write("src/reserved/code.py", "pass\n")
        self.activate()
        self.start_branch()
        git(["mv", "src/reserved/code.py", "src/code.py"], self.root)
        self.commit("rename out")
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("src/reserved/code.py", findings[0])

    def test_rename_destination_under_reserved_prefix_fails(self):
        self.write("src/code.py", "pass\n")
        self.activate()
        self.start_branch()
        os.makedirs(os.path.join(self.root, "src", "reserved"), exist_ok=True)
        git(["mv", "src/code.py", "src/reserved/code.py"], self.root)
        self.commit("rename in")
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("src/reserved/code.py", findings[0])

    def test_marker_removal_with_reserved_rename_fails(self):
        self.write("src/reserved/code.py", "pass\n")
        self.activate()
        self.start_branch()
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        git(["mv", "src/reserved/code.py", "src/code.py"], self.root)
        self.commit("remove gate and rename out")
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("src/reserved/code.py", findings[0])

    def test_baseline_and_current_prefix_union_is_enforced(self):
        old_marker = self.MARKER.replace("src/reserved/", "src/old/")
        new_marker = self.MARKER.replace("src/reserved/", "src/new/")
        self.activate(old_marker)
        self.write("docs/roadmap.md", self.roadmap_with_marker(new_marker))
        self.write("src/old/old.py", "pass\n")
        self.write("src/new/new.py", "pass\n")
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertIn("src/old/old.py", findings[0])
        self.assertIn("src/new/new.py", findings[0])

    def test_change_after_marker_removal_passes(self):
        self.activate()
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        self.commit("remove gate on main")
        self.write("src/reserved/code.py", "pass\n")
        self.commit_branch()
        self.assertEqual(docs_checks.check_gate(self.root, self.baseline), [])

    def test_unavailable_baseline_fails_closed(self):
        self.write("docs/roadmap.md", self.roadmap_with_marker())
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, "missing-baseline")
        self.assertTrue(findings)
        self.assertIn("merge base", findings[0])

    def test_unrelated_baseline_with_roadmap_fails_closed(self):
        git(["checkout", "-q", "--orphan", "unrelated"], self.root)
        self.write("docs/roadmap.md", self.ROADMAP_V1)
        self.commit("unrelated roadmap")
        git(["checkout", "-q", "main"], self.root)
        self.write("src/maintenance.py", "pass\n")
        self.commit_branch()
        findings = docs_checks.check_gate(self.root, "unrelated")
        self.assertTrue(findings)
        self.assertIn("merge base", findings[0])

    def test_changed_path_resolution_failure_fails_closed(self):
        self.write("docs/roadmap.md", self.roadmap_with_marker())
        self.write("src/reserved/new.py", "pass\n")
        self.commit_branch()
        with mock.patch.object(
                docs_checks, "_changed_paths", return_value=None):
            findings = docs_checks.check_gate(self.root, self.baseline)
        self.assertTrue(findings)
        self.assertIn("changed paths could not be resolved", findings[0])
```

- [ ] **Step 2: Run the phase-aware gate tests to verify RED**

Run:

```powershell
python tools/checks/tests/test_docs_checks.py TestGate -v
```

Expected: FAIL for the legacy-phrase retirement, current-only and baseline-only reservations, rename endpoints, union, unavailable or unrelated merge bases, and forced changed-path failure because `check_gate()` still implements the old current-roadmap phrase switch.

- [ ] **Step 3: Add strict gate baseline resolution and decode status-aware changed paths**

Add these gate-specific helpers immediately before `check_gate()`. Do not change `_resolve_baseline()` or `_baseline_roadmap()`; their lenient fallback remains the gate-change-log contract.

```python
def _resolve_gate_base(root, baseline_ref):
    try:
        proc = subprocess.run(
            ["git", "merge-base", baseline_ref, "HEAD"],
            cwd=root, capture_output=True, text=True, encoding="utf-8",
        )
    except OSError:
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
    except OSError:
        return None
    return proc.stdout if proc.returncode == 0 else None


def _changed_paths(root, base):
    try:
        proc = subprocess.run(
            ["git", "diff", "--name-status", "-z", "--find-renames",
             f"{base}...HEAD"],
            cwd=root, capture_output=True, text=True, encoding="utf-8",
        )
    except OSError:
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
```

- [ ] **Step 4: Replace the global phrase switch with reservation-union enforcement**

Replace the module-level check-summary lines for item 5 with the exact text below, delete the old `GATE_MARKER` constant, and replace `check_gate()` with the following implementation:

```python
5. gate      — roadmap markers reserve phase-scoped implementation paths.
```

Then replace `check_gate()` with:

```python
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
        matches = [
            path for path in changed
            if any(path.startswith(prefix) for prefix in prefixes)
        ]
        if matches:
            blocked[phase] = matches
    return [
        f"gate: Phase {phase} reserved paths block changed files: "
        f"{', '.join(paths)}"
        for phase, paths in sorted(blocked.items())
    ]
```

- [ ] **Step 5: Run focused and full documentation checks to verify GREEN**

Run:

```powershell
python tools/checks/tests/test_docs_checks.py TestGate -v
python -m unittest discover -s tools/checks/tests -p "test_docs_checks.py" -v
python tools/checks/docs_checks.py
```

Expected: every phase-aware test PASS, the complete documentation-check suite PASS, and `docs_checks: clean` for the repository because no active marker is added.

- [ ] **Step 6: Commit scoped enforcement**

```powershell
git add tools/checks/docs_checks.py tools/checks/tests/test_docs_checks.py
git commit -m "feat: enforce phase-aware documentation gate (#89)"
```

## Final Verification and Review Gate

- [ ] Run the repository-proportioned Python suites:

```powershell
python -m unittest discover -s tools/checks/tests -p "test_*.py" -v
python -m unittest discover -s tools/agent-coordination/tests -p "test_*.py" -v
python -m unittest discover -s tools/agent-assets/tests -p "test_*.py" -v
```

- [ ] Restore and run the .NET suite without introducing dependency changes:

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode
dotnet test AutoGIS.Civil3D.sln -c Release --no-restore
dotnet format AutoGIS.Civil3D.sln --verify-no-changes
```

- [ ] Verify the exact change boundary and whitespace:

```powershell
git diff --check origin/main...HEAD
git diff --name-status origin/main...HEAD
```

Expected changed files exactly:

```text
M tools/checks/docs_checks.py
M tools/checks/tests/test_docs_checks.py
```

- [ ] Confirm `docs/roadmap.md`, workflow files, dependencies, and product source are unchanged, then request an independent Standards + Spec review of the exact pushed head. Resolve all Critical/Important findings, rerun this gate on the final head, and close issue #89 only when the implementation PR merges.
