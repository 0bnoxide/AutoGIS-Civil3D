# Codex Project-Hook Trust Marker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Report Codex project-hook trust as verified only when checked-in evidence has a valid date and both required passing checks.

**Architecture:** `coordination.py` gains a tiny parser for one Markdown record in the current worktree. `cmd_doctor` turns its result into either a dated verified finding or the existing actionable unverified finding; no interactive Codex UI is automated.

**Tech Stack:** Python 3 standard library, `unittest`, Markdown.

## Global Constraints

- Trust evidence is advisory; it never changes main-protection behavior or makes `doctor` fail.
- Evidence is never inferred from `.codex/hooks.json` or file presence alone.
- The evidence path is `docs/verification/codex-project-hook-trust.md`.
- A valid record has one ISO date plus `Hooks inspection: passed` and `Activation probe: passed`.
- Tests use temporary repositories only.

---

## File structure

- Modify: `tools/agent-coordination/coordination.py` — parse and report evidence.
- Modify: `tools/agent-coordination/tests/test_coordination.py` — test complete, missing, incomplete, and malformed evidence.
- Create: `docs/verification/codex-project-hook-trust.md` — record the 2026-08-05 verification.

### Task 1: Test and implement the evidence parser

**Files:**

- Modify: `tools/agent-coordination/tests/test_coordination.py`
- Modify: `tools/agent-coordination/coordination.py`

**Interfaces:**

- Produces: `codex_hook_trust_date(repo) -> str | None`.
- Consumes: `Repo.worktree_root`; `cmd_doctor(repo)` consumes the returned date.

- [ ] **Step 1: Write failing tests**

Add a test helper that writes `docs/verification/codex-project-hook-trust.md` below `self.repo_path`. Add a test whose complete content is `Verification date: 2026-08-05`, `Hooks inspection: passed`, and `Activation probe: passed`, and assert `cmd_doctor` returns `ALLOW` and its captured output contains `Codex project-hook trust: verified (2026-08-05)`. Add tests for a missing file, a file with only `Verification date: invalid`, and a file missing one passed marker; each must return `ALLOW` and contain `Codex project-hook trust: unverified`.

- [ ] **Step 2: Verify RED**

Run `python -m unittest tools.agent-coordination.tests.test_coordination`. The complete-evidence assertion must fail because `cmd_doctor` currently emits unconditional `unverified`.

- [ ] **Step 3: Add the minimal parser**

Add `codex_hook_trust_date(repo)` immediately before `cmd_doctor`. It reads `repo.worktree_root/docs/verification/codex-project-hook-trust.md`; on `OSError`, returns `None`. Build a set of newline-stripped lines, extract the sole `Verification date: ` value, require both exact passed-marker lines, and validate the date with `_dt.date.fromisoformat`. Return the date only when every condition holds.

Replace the unconditional trust finding in `cmd_doctor` with a `trust_date = codex_hook_trust_date(repo)` branch: append `Codex project-hook trust: verified ({trust_date})` when it is not `None`; otherwise append the existing unverified wording.

- [ ] **Step 4: Verify GREEN**

Run `python -m unittest tools.agent-coordination.tests.test_coordination`. Expected: exit code 0; valid evidence is dated verified and every invalid/missing case is unverified while `doctor` remains successful.

- [ ] **Step 5: Commit**

Run `git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py` and `git commit -m "feat: verify recorded Codex hook trust evidence"`.

### Task 2: Record the verification evidence

**Files:**

- Create: `docs/verification/codex-project-hook-trust.md`

**Interfaces:**

- Consumes: the exact marker syntax from `codex_hook_trust_date(repo)`.
- Produces: a record that makes `doctor` output `verified (2026-08-05)`.

- [ ] **Step 1: Create the evidence record**

Create a Markdown file titled `Codex project-hook trust verification`, followed by the exact lines `Verification date: 2026-08-05`, `Hooks inspection: passed`, and `Activation probe: passed`. State that `/hooks` inspected `.codex/hooks.json` and a harmless main-targeting write probe was denied before a filesystem mutation.

- [ ] **Step 2: Verify the real record**

Run `python tools/agent-coordination/coordination.py doctor`. Expected: output includes `Codex project-hook trust: verified (2026-08-05)`; do not suppress unrelated advisory findings.

- [ ] **Step 3: Run regression tests and commit**

Run `python -m unittest tools.agent-coordination.tests.test_coordination`; expect exit code 0. Then run `git add docs/verification/codex-project-hook-trust.md` and `git commit -m "docs: record Codex hook trust verification"`.

### Task 3: Final validation

**Files:**

- Verify: `tools/agent-coordination/coordination.py`
- Verify: `tools/agent-coordination/tests/test_coordination.py`
- Verify: `docs/verification/codex-project-hook-trust.md`

**Interfaces:**

- Verifies: only complete valid evidence produces the dated verified result.

- [ ] **Step 1: Run formatting and suite checks**

Run `git diff main --check` and `python -m unittest tools.agent-coordination.tests.test_coordination`. Expected: both exit code 0 and no test failures.

- [ ] **Step 2: Confirm diagnostic output and scope**

Run `python tools/agent-coordination/coordination.py doctor`, `git status --short`, and `git diff main --name-only`. Expected: `doctor` includes the dated verified trust result; the changed scope is only the parser, tests, marker, design, and plan.
