# Phase 3 Producer Leg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Phase 3 compatibility harness: the pinned AutoGIS checkout's `autogis handoff` command emits a contract-v1 package from a fixture-derived LandXML on the CI runner, and the shipped validator accepts it with exit 0 — zero warnings (gate issue #78, bundle item 1).

**Architecture:** One new stdlib script runs the producer leg — extract the fixture's `surface.landxml`, invoke the installed producer CLI with explicit known-datum metadata, validate the emitted ZIP with the Release validator, require exit exactly 0. The existing `compat-autogis` workflow gains the pinned AutoGIS checkout (read-only deploy key), a Python setup, a base-deps install, and the producer step. The three diagnostics deferred on #78 land here: failure output includes stdout, subprocesses get timeouts, and the job pins UTF-8 streams.

**Tech Stack:** Python 3 stdlib (`subprocess`, `zipfile`, `tempfile`, `pathlib`), GitHub Actions (`actions/checkout@v4` cross-repo with `ssh-key`, `actions/setup-python@v5`), existing fixture corpus (read-only), AutoGIS pinned at the ADR-0128 feature merge.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-11-phase-3-producer-adoption-design.md` §Compatibility harness (steps 1–5). Gate evidence collects on issue #78.
- Execution starts only after the owner approves this plan (merges this plan PR). Work then happens on a fresh branch `claude/phase-3-producer-impl` off synced `main`, in a claimed worktree, with claims (session id = the harness session UUID) on that branch before writing.
- **AutoGIS pin:** `07a8deeac5560440b2f0b05ca8087b8b05a1fac9` — the merge commit of AutoGIS PR #480 (`autogis handoff`, ADR-0128). The pin appears in exactly one place (the workflow job's `AUTOGIS_PIN` env); advancing it is an ordinary PR editing that line, per the spec.
- Producer invocation is the ADR-0128 surface, verbatim flags: `python -m autogis handoff --input … --output … --vertical-unit metre --vertical-datum-authority EPSG --vertical-datum-code 5703 --vertical-datum-name "NAVD88 height" --source-commit <pin>`. The fixture-derived input is metric (EPSG:26913), so `metre` and the known datum trio make exit 0 / zero warnings reachable; the unknown-datum path is never gate evidence.
- Validator CLI exit codes (from `src/AutoGIS.Civil3D.Handoff.Cli/CliApplication.cs`, verbatim): `0` Valid, `1` Invalid, `2` ValidWithWarnings, `3` usage/operational failure. The zero-warning gate means requiring exit exactly `0`, never "nonzero is fine". The negative control stays in `tools/checks/compat_smoke.py` (unchanged remit).
- Deploy key: the workflow reads `secrets.AUTOGIS_DEPLOY_KEY` (read-only deploy key on `0bnoxide/AutoGIS`, provisioned via issue #75). Never echo or persist the key beyond `actions/checkout`.
- Deferred diagnostics from #78 (comment 5250649659), all land in this plan: print `proc.stdout` on any leg failure; `timeout=600` on every `subprocess.run`; UTF-8 stream pinning via job-level `PYTHONUTF8: "1"` (covers both check scripts — no per-script `reconfigure` code).
- Python subprocess calls pin `encoding="utf-8", errors="replace"` (Windows defaults to cp1252).
- Fixtures are consumed read-only; no contract, fixture, or validator changes.
- Stdlib only — no PyYAML/requests/etc. in `tools/checks/`.
- Commits: conventional style referencing `(#78)`, trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Local verification runs from the worktree root and requires `dotnet build AutoGIS.Civil3D.sln -c Release` first. For the local producer run, install the producer once into the current Python from the local AutoGIS checkout: `python -m pip install C:\Users\ichbi\AutoGIS` (any recent head proves the plumbing locally; the pin is proven in CI).

---

### Task 1: Producer-leg check script + smoke diagnostics retrofit

**Files:**
- Create: `tools/checks/compat_producer.py`
- Modify: `tools/checks/compat_smoke.py`

**Interfaces:**
- Consumes: built Release CLI at `src/AutoGIS.Civil3D.Handoff.Cli` (via `dotnet run --no-build`); fixture `fixtures/v1/valid/known-vertical-datum.zip` (read-only); an installed `autogis` package (CI installs the pinned checkout; locally `pip install C:\Users\ichbi\AutoGIS`); env var `AUTOGIS_PIN` holding the pinned commit sha.
- Produces: `python tools/checks/compat_producer.py` exiting `0` when the producer package validates with exit exactly 0, `1` on any failure (with stdout+stderr of the failing subprocess printed). Task 2's workflow calls it under exactly this path and contract.

- [ ] **Step 1: Build the solution so the validator is runnable**

Run: `dotnet build AutoGIS.Civil3D.sln -c Release`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Write the producer-leg script**

Create `tools/checks/compat_producer.py`:

```python
"""Compat-harness producer leg (Phase 3 gate issue #78).

The pinned AutoGIS checkout's `autogis handoff` command (ADR-0128 in that
repository) emits a contract-v1 package from a LandXML extracted from the
fixture corpus, with explicit metadata including a known vertical datum.
The shipped validator must accept the emitted package with exit exactly 0
(zero warnings; per the spec the unknown-datum path is never gate
evidence). Requires the `autogis` package to be installed and AUTOGIS_PIN
to hold the pinned commit sha (recorded once in the compat workflow).
"""
import os
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "fixtures" / "v1" / "valid" / "known-vertical-datum.zip"
VALIDATOR = [
    "dotnet", "run", "-c", "Release", "--no-build",
    "--project", str(ROOT / "src" / "AutoGIS.Civil3D.Handoff.Cli"), "--",
]


def _run(cmd):
    return subprocess.run(
        cmd, capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=600,
    )


def _fail(label, proc):
    print(f"FAIL: {label} -> exit {proc.returncode}")
    if proc.stdout:
        print(proc.stdout)
    if proc.stderr:
        print(proc.stderr, file=sys.stderr)
    return 1


def main():
    pin = os.environ.get("AUTOGIS_PIN", "")
    if not re.fullmatch(r"[0-9a-f]{7,64}", pin):
        print("FAIL: AUTOGIS_PIN must hold the pinned AutoGIS commit sha")
        return 1
    with tempfile.TemporaryDirectory() as tmp:
        source = Path(tmp) / "source.landxml"
        with zipfile.ZipFile(FIXTURE) as zf:
            source.write_bytes(zf.read("surface.landxml"))
        package = Path(tmp) / "producer-package.zip"
        producer = _run([
            sys.executable, "-m", "autogis", "handoff",
            "--input", str(source),
            "--output", str(package),
            "--vertical-unit", "metre",
            "--vertical-datum-authority", "EPSG",
            "--vertical-datum-code", "5703",
            "--vertical-datum-name", "NAVD88 height",
            "--source-commit", pin,
        ])
        if producer.returncode != 0:
            return _fail("producer emission", producer)
        print(f"ok: producer emitted a package at pin {pin[:12]}")
        verdict = _run(VALIDATOR + [str(package)])
        if verdict.returncode != 0:
            return _fail(
                "validator on producer package (want 0, zero warnings)",
                verdict)
        print("ok: validator accepted the producer package -> exit 0")
    print("compat_producer: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: Retrofit the deferred diagnostics into `tools/checks/compat_smoke.py`**

Two mechanical edits, nothing else:

1. In the `subprocess.run(...)` call, add `timeout=600` after `errors="replace"`.
2. In the failure branch, print stdout too — replace:

```python
        if not ok and proc.stderr:
            print(proc.stderr, file=sys.stderr)
```

with:

```python
        if not ok:
            if proc.stdout:
                print(proc.stdout)
            if proc.stderr:
                print(proc.stderr, file=sys.stderr)
```

- [ ] **Step 4: Run both scripts locally**

Run, from the worktree root (PowerShell):

```
python -m pip install C:\Users\ichbi\AutoGIS
$env:AUTOGIS_PIN = "07a8deeac5560440b2f0b05ca8087b8b05a1fac9"
python tools/checks/compat_producer.py
python tools/checks/compat_smoke.py
```

Expected: `compat_producer.py` prints the two `ok:` lines then `compat_producer: clean`, exit 0 (`$LASTEXITCODE` is 0). `compat_smoke.py` still prints its three `ok:` lines and `compat_smoke: clean`. (The local AutoGIS install may be newer than the pin — that proves the plumbing; the pin itself is proven by CI.)

- [ ] **Step 5: Verify the producer leg can fail**

Run: `Remove-Item Env:AUTOGIS_PIN` then `python tools/checks/compat_producer.py` — expect `FAIL: AUTOGIS_PIN must hold...`, exit 1. Then set `$env:AUTOGIS_PIN` back, temporarily change the script's final validator check from `!= 0` to `!= 2`, run, confirm `FAIL: validator on producer package ... -> exit 0` and exit 1, then restore `!= 0` and re-run to confirm `compat_producer: clean`.

- [ ] **Step 6: Commit**

```bash
git add tools/checks/compat_producer.py tools/checks/compat_smoke.py
git commit -m "feat(compat): producer-leg check + smoke diagnostics (#78)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Workflow producer leg

**Files:**
- Modify: `.github/workflows/compat-autogis.yml`

**Interfaces:**
- Consumes: `tools/checks/compat_producer.py` from Task 1 under exactly that path; `secrets.AUTOGIS_DEPLOY_KEY`.
- Produces: the `harness` job runs validator legs then the producer leg on every trigger; the pin lives only in the job-level `AUTOGIS_PIN` env.

- [ ] **Step 1: Edit the workflow**

Apply exactly these changes to `.github/workflows/compat-autogis.yml`:

1. Replace the header comment (the four `#` lines under `name:`) with:

```yaml
# Phase 3 cross-repository compatibility harness (gate issue #78).
# Validator legs (clean / warning / negative control) plus the producer
# leg: AutoGIS pinned at AUTOGIS_PIN is checked out read-only (deploy key,
# issue #75), installed base-deps-only, and its `autogis handoff` output
# must validate with exit 0 - zero warnings. Advancing the pin is an
# ordinary PR editing AUTOGIS_PIN below (spec: docs/superpowers/specs/
# 2026-08-11-phase-3-producer-adoption-design.md).
```

2. In the `push.paths` list, add after the `compat_smoke.py` line:

```yaml
      - tools/checks/compat_producer.py
```

3. Give the `harness` job the env block (between `runs-on:` and `steps:`):

```yaml
    env:
      PYTHONUTF8: "1"
      AUTOGIS_PIN: 07a8deeac5560440b2f0b05ca8087b8b05a1fac9
```

4. Append these steps after the existing `Validator legs` step:

```yaml
      - name: Checkout AutoGIS at the pin (read-only deploy key)
        uses: actions/checkout@v4
        with:
          repository: 0bnoxide/AutoGIS
          ref: ${{ env.AUTOGIS_PIN }}
          ssh-key: ${{ secrets.AUTOGIS_DEPLOY_KEY }}
          path: autogis-src
      - uses: actions/setup-python@v5
        with:
          python-version: '3.11'
      - name: Install producer (base dependencies only)
        run: python -m pip install ./autogis-src
      - name: Producer leg (emit + validate, zero warnings)
        env:
          AUTOGIS_RUN_HISTORY: off
        run: python tools/checks/compat_producer.py
```

There is no local workflow-YAML runner; the executable verification is Task 3's green `compat-autogis` run, which exercises the real Actions parser, the deploy-key checkout, and every step.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/compat-autogis.yml
git commit -m "feat(compat): wire the pinned producer leg into compat-autogis (#78)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Ship and record

**Files:** none (push + bookkeeping only).

**Interfaces:**
- Consumes: Tasks 1-2 committed on `claude/phase-3-producer-impl`.
- Produces: an open PR referencing #78; a green `compat-autogis` run on the PR branch including the producer leg; after the owner merges, a green run on `main` and the #78 bundle item 1 checkbox ticked with both run URLs.

- [ ] **Step 1: Push and open the PR**

```bash
git push -u origin claude/phase-3-producer-impl
gh pr create --title "feat(compat): Phase 3 producer leg (#78)" --body "Producer leg of the Phase 3 compatibility harness per the approved plan docs/superpowers/plans/2026-08-12-phase-3-producer-leg.md: pinned AutoGIS checkout (07a8deea, PR 480 / ADR-0128) via the read-only deploy key, base-deps install, fixture-derived emission with explicit known-datum metadata, validator gate at exit exactly 0. Also lands the three diagnostics deferred on #78."
```

- [ ] **Step 2: Confirm the workflow ran green on the branch push (producer leg included)**

Run: `gh run list --workflow compat-autogis --branch claude/phase-3-producer-impl --limit 1`
Expected: one run, conclusion `success`, and the run log shows `compat_producer: clean`. If the paths filter didn't trigger it, run `gh workflow run compat-autogis --ref claude/phase-3-producer-impl` and re-check.

- [ ] **Step 3: Request review and hand off the merge**

Mark the PR ready for review per the repo's review mechanics (Codex/gitar respond on ready-for-review; resolve threads before merge — the main ruleset requires thread resolution). Merge sign-off is the owner's.

- [ ] **Step 4: After the owner merges — record the gate evidence**

Confirm the push-triggered `compat-autogis` run on `main` is green, then on issue #78: tick bundle item 1, citing the main-branch run URL and the pin `07a8deea` recorded in the workflow.
