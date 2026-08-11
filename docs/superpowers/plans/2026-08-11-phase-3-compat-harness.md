# Phase 3 Compat Harness (Skeleton) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the Phase 3 cross-repository compatibility workflow with its validator legs — clean pass, warning distinguishability, negative control — so the producer leg drops into a proven harness once AutoGIS's ADR names its CLI (gate issue #78).

**Architecture:** One stdlib Python script asserts the shipped validator CLI's exit codes discriminate clean / warning / invalid fixture packages when invoked as a subprocess — the exact invocation shape the compat workflow uses. A new GitHub Actions workflow builds the solution and runs that script. The producer leg (pinned AutoGIS checkout, writer-path re-emission, validation of the emitted package) is a follow-up plan blocked on the AutoGIS ADR and the deploy key (issue #75); it is NOT in this plan.

**Tech Stack:** Python 3 stdlib (`subprocess`, `pathlib`), GitHub Actions (`windows-latest`, `actions/setup-dotnet@v4`, .NET 8.0.x), existing fixture corpus (read-only).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-11-phase-3-producer-adoption-design.md`. Gate evidence collects on issue #78.
- Execution starts only after the owner approves this plan (merges the spec+plan PR). Work on a fresh branch `claude/phase-3-compat-harness` off synced `main`, in a claimed worktree, with claims (session id = the harness session UUID) on that branch plus literal file claims on `tools/checks/compat_smoke.py` and `.github/workflows/compat-autogis.yml` before writing.
- Validator CLI exit codes (from `src/AutoGIS.Civil3D.Handoff.Cli/CliApplication.cs`, verbatim): `0` Valid, `1` Invalid, `2` ValidWithWarnings, `3` usage/operational failure. The zero-warning gate means requiring exit exactly `0`, never "nonzero is fine".
- Fixtures are consumed read-only; no contract, fixture, or validator changes in this plan.
- Python subprocess calls pin `encoding="utf-8"` (Windows defaults to cp1252 and garbles CLI output).
- CI mirrors house style: `windows-latest`, `actions/setup-dotnet@v4` with `dotnet-version: 8.0.x`, locked-mode restore (see `.github/workflows/ci.yml`).
- Commits: conventional style referencing `(#78)`, trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Local verification commands run from the worktree root and require `dotnet build AutoGIS.Civil3D.sln -c Release` to have been run first.

---

### Task 1: Validator exit-code smoke script

**Files:**
- Create: `tools/checks/compat_smoke.py`

**Interfaces:**
- Consumes: built Release CLI at `src/AutoGIS.Civil3D.Handoff.Cli` (via `dotnet run --no-build`); fixture packages `fixtures/v1/valid/known-vertical-datum.zip`, `fixtures/v1/valid/unknown-vertical-datum.zip`, `fixtures/v1/invalid/checksum.zip`.
- Produces: `python tools/checks/compat_smoke.py` exiting `0` on success, `1` on any mismatch, printing one line per case. Task 2's workflow calls it under exactly this path and contract.

- [ ] **Step 1: Build the solution so the CLI is runnable**

Run: `dotnet build AutoGIS.Civil3D.sln -c Release`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Write the script**

Create `tools/checks/compat_smoke.py`:

```python
"""Compat-harness smoke: the shipped validator CLI, invoked as a subprocess
exactly as the compat workflow invokes it, discriminates clean / warning /
invalid packages by exit code (0 / 2 / 1 per CliApplication.cs).

Phase 3 gate evidence requires exit exactly 0 (zero warnings) for the
producer package; the warning and invalid legs prove that requirement can
fail, so a green run is meaningful (spec: negative control).
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLI = [
    "dotnet", "run", "-c", "Release", "--no-build",
    "--project", str(ROOT / "src" / "AutoGIS.Civil3D.Handoff.Cli"), "--",
]

CASES = (
    ("fixtures/v1/valid/known-vertical-datum.zip", 0),
    ("fixtures/v1/valid/unknown-vertical-datum.zip", 2),
    ("fixtures/v1/invalid/checksum.zip", 1),
)


def main():
    failures = 0
    for rel, expected in CASES:
        proc = subprocess.run(
            CLI + [str(ROOT / rel)], capture_output=True,
            text=True, encoding="utf-8", errors="replace",
        )
        ok = proc.returncode == expected
        failures += not ok
        print(f"{'ok' if ok else 'FAIL'}: {rel} -> exit {proc.returncode}"
              f" (want {expected})")
        if not ok and proc.stderr:
            print(proc.stderr, file=sys.stderr)
    if failures:
        return 1
    print("compat_smoke: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: Run it and verify all three legs**

Run: `python tools/checks/compat_smoke.py`
Expected output (order fixed):

```text
ok: fixtures/v1/valid/known-vertical-datum.zip -> exit 0 (want 0)
ok: fixtures/v1/valid/unknown-vertical-datum.zip -> exit 2 (want 2)
ok: fixtures/v1/invalid/checksum.zip -> exit 1 (want 1)
compat_smoke: clean
```

Exit code `0` (`echo $LASTEXITCODE` in PowerShell prints `0`).

- [ ] **Step 4: Verify the script itself can fail**

Run: `python tools/checks/compat_smoke.py` with one expectation temporarily flipped — edit the `CASES` line for `checksum.zip` from `1` to `0`, run, confirm the output shows `FAIL: fixtures/v1/invalid/checksum.zip -> exit 1 (want 0)` and the script exits `1`, then restore the line to `1` and re-run to confirm `compat_smoke: clean` again.

- [ ] **Step 5: Commit**

```bash
git add tools/checks/compat_smoke.py
git commit -m "feat(compat): validator exit-code smoke for the Phase 3 harness (#78)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Compat workflow

**Files:**
- Create: `.github/workflows/compat-autogis.yml`

**Interfaces:**
- Consumes: `tools/checks/compat_smoke.py` from Task 1, under exactly that path.
- Produces: workflow `compat-autogis` runnable via `workflow_dispatch` and on pushes touching the harness's inputs. The follow-up producer-leg plan extends THIS file with the pinned AutoGIS checkout and producer steps.

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/compat-autogis.yml`:

```yaml
name: compat-autogis

# Phase 3 cross-repository compatibility harness (gate issue #78).
# Validator legs only until the producer leg lands: the AutoGIS checkout
# and producer invocation are blocked on the AutoGIS-side ADR naming its
# CLI and on the read-only deploy key (issue #75).
on:
  workflow_dispatch:
  push:
    paths:
      - .github/workflows/compat-autogis.yml
      - contract/**
      - fixtures/**
      - src/**
      - tools/checks/compat_smoke.py
      # Shared build inputs the restore/build steps consume:
      - AutoGIS.Civil3D.sln
      - Directory.Build.props
      - Directory.Packages.props
      - global.json

jobs:
  harness:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - name: Restore
        run: dotnet restore AutoGIS.Civil3D.sln --locked-mode
      - name: Build
        run: dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
      - name: Validator legs (clean / warning / negative control)
        run: python tools/checks/compat_smoke.py
```

There is no local workflow-YAML check in this repo's stdlib-only Python
tooling (no PyYAML, no actionlint); the workflow's executable verification
is Task 3 Step 2 — the branch push must produce a green `compat-autogis`
run, which exercises the real Actions parser and every step.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/compat-autogis.yml
git commit -m "feat(compat): Phase 3 compat workflow, validator legs (#78)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Ship and record

**Files:** none (push + bookkeeping only).

**Interfaces:**
- Consumes: Tasks 1-2 committed on `claude/phase-3-compat-harness`.
- Produces: an open PR referencing #78; a green `compat-autogis` run on the PR branch; a comment on #78 linking the run.

- [ ] **Step 1: Push and open the PR**

```bash
git push -u origin claude/phase-3-compat-harness
gh pr create --title "feat(compat): Phase 3 compat harness skeleton (#78)" --body "Validator legs of the Phase 3 compatibility harness per the approved plan docs/superpowers/plans/2026-08-11-phase-3-compat-harness.md: exit-code smoke (clean 0 / warning 2 / invalid 1) plus the compat-autogis workflow. Producer leg follows once the AutoGIS ADR names its CLI and #75 is set up."
```

- [ ] **Step 2: Confirm the workflow ran green on the branch push**

Run: `gh run list --workflow compat-autogis --branch claude/phase-3-compat-harness --limit 1`
Expected: one run, conclusion `success`. If it did not trigger (paths filter), run `gh workflow run compat-autogis --ref claude/phase-3-compat-harness` and re-check.

- [ ] **Step 3: Record on the gate issue**

Comment on issue #78: the negative-control leg exists and is green, linking the successful run URL and naming the two blocked prerequisites for the producer leg (AutoGIS ADR; #75 deploy key). This partially satisfies bundle item 2; items 1 and 3 remain open.

- [ ] **Step 4: Request review**

Mark the PR ready for review (or comment `@codex review`) per the repo's review mechanics; merge sign-off is the owner's.
