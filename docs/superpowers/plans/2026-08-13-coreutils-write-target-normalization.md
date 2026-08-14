# Coreutils Write-Target Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the main-protection adapter capture coreutils write/delete/copy targets when the command token ends in Windows-stripped dots or spaces, without changing adjacent path, case, or PowerShell semantics.

**Architecture:** Normalize `argv[0]` once inside `_argv_write_targets` with `rstrip(". ")` and route only the existing coreutils comparisons through that local token. Keep the original token for PowerShell alias matching, and protect the boundary with a complete helper-level matrix plus representative end-to-end denial tests.

**Tech Stack:** Python 3 standard library, `unittest`, Git, GitHub Actions

## Global Constraints

- Follow `docs/agent-guide.md` and `docs/collaboration.md`; `main` is read-only, and the implementation must run in a claimed linked worktree based on current `origin/main`.
- Use branch `codex/issue-81-coreutils-normalization` and worktree `.worktrees/codex+issue-81-coreutils-normalization`; claim both plus the two modified files before editing.
- Before reading any repository file, run `sonar analyze secrets <path>` and stop if the scanner reports a secret.
- Modify only `tools/agent-coordination/coordination.py` and `tools/agent-coordination/tests/test_coordination.py`.
- Use test-driven development: add the regression tests, observe the positive cases fail, then implement the minimal normalization and rerun the same tests.
- Normalize only with `argv[0].rstrip(". ")`; do not lowercase, remove path components, call `_git_executable_name`, add a helper, or add a dependency.
- Keep `low = argv[0].lower()` based on the original token for PowerShell cmdlet and alias matching.
- Preserve operand parsing and target ordering, including destination-before-source ordering for `mv`.
- Keep uppercase and pathed coreutils outside the matcher; keep suffixed PowerShell aliases such as `del.` and `copy.` unmatched.
- Treat runtime exploitability as out of scope; parser-level regression tests are the acceptance proof for this theoretical hardening.
- Require an independent adversarial review because deletion paths have no commit-hook backstop, even though issue #81 is low severity.

---

### Task 1: Add the red regression tests

**Files:**
- Modify: `tools/agent-coordination/tests/test_coordination.py:523`
- Reference only: `tools/agent-coordination/coordination.py:674-696`

**Interfaces:**
- Consumes: `_argv_write_targets(argv, ps)` for raw target extraction and `deny_reason_for_shell(command, cwd, repo_hint, ps=False)` for the final denial decision.
- Produces: three `TestMainRule` test methods whose names contain `coreutils`, enabling a focused `unittest -k coreutils` cycle.

- [ ] **Step 1: Add the extraction matrix and boundary tests**

Insert these methods in `TestMainRule` immediately before the existing `test_tee_in_pipeline_stage_denied` method:

```python
    def test_coreutils_trailing_dot_space_targets(self):
        cases = (
            ("tee", ("seed.txt",), ["seed.txt"]),
            ("sed", ("-i", "seed.txt"), ["seed.txt"]),
            ("dd", ("of=seed.txt",), ["seed.txt"]),
            ("truncate", ("seed.txt",), ["seed.txt"]),
            ("rm", ("seed.txt",), ["seed.txt"]),
            ("unlink", ("seed.txt",), ["seed.txt"]),
            ("shred", ("seed.txt",), ["seed.txt"]),
            ("cp", ("other.txt", "seed.txt"), ["seed.txt"]),
            ("mv", ("other.txt", "seed.txt"),
             ["seed.txt", "other.txt"]),
            ("install", ("other.txt", "seed.txt"), ["seed.txt"]),
        )
        for command, operands, expected in cases:
            for suffix in (".", " "):
                with self.subTest(command=command, suffix=suffix):
                    self.assertEqual(
                        coordination._argv_write_targets(
                            [command + suffix, *operands], ps=False),
                        expected)

    def test_coreutils_suffix_normalization_preserves_boundaries(self):
        for command in ("RM.", "RM ", "/usr/bin/rm.",
                        r"C:\Program Files\Git\usr\bin\rm."):
            with self.subTest(command=command):
                self.assertEqual(
                    coordination._argv_write_targets(
                        [command, "seed.txt"], ps=False),
                    [])

        powershell_cases = (
            (["DEL", "seed.txt"], ["seed.txt"]),
            (["COPY", "other.txt", "seed.txt"], ["seed.txt"]),
            (["del.", "seed.txt"], []),
            (["del ", "seed.txt"], []),
            (["copy.", "other.txt", "seed.txt"], []),
            (["copy ", "other.txt", "seed.txt"], []),
        )
        for argv, expected in powershell_cases:
            with self.subTest(argv=argv):
                self.assertEqual(
                    coordination._argv_write_targets(argv, ps=True),
                    expected)

    def test_coreutils_suffix_forms_denied_on_main(self):
        for command in ("tee. seed.txt", "rm. seed.txt",
                        "cp. other.txt seed.txt"):
            with self.subTest(command=command):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    command, self.repo_path, self.repo))
```

- [ ] **Step 2: Run the focused tests and verify the red state**

```powershell
sonar analyze secrets tools/agent-coordination
$env:PYTHONDONTWRITEBYTECODE = "1"
python -m unittest discover -s tools/agent-coordination/tests `
  -p "test_coordination.py" -k coreutils -v
```

Expected: the command exits nonzero. The trailing-dot/space extraction subtests report empty actual targets, and the end-to-end denial subtests report `None`; the preserved-boundary method passes. Do not weaken those assertions to make the red run green.

### Task 2: Apply the minimal command-token normalization

**Files:**
- Modify: `tools/agent-coordination/coordination.py:674-696`
- Test: `tools/agent-coordination/tests/test_coordination.py`

**Interfaces:**
- Consumes: the regression contract from Task 1.
- Produces: `_argv_write_targets(argv, ps)` with coreutils matching based on `cmd = argv[0].rstrip(". ")` and PowerShell matching based on the raw token.

- [ ] **Step 1: Replace `_argv_write_targets` with the minimal implementation**

Replace the complete function with:

```python
def _argv_write_targets(argv, ps):
    """Return raw target operands for the write form represented by argv."""
    cmd = argv[0].rstrip(". ")
    targets = []
    if cmd == "tee":
        targets += [arg for arg in argv[1:] if not arg.startswith("-")]
    if cmd == "sed" and any(arg.startswith("-i") for arg in argv[1:]):
        targets += [arg for arg in argv[1:] if not arg.startswith("-")][-1:]
    if cmd == "dd":
        targets += [arg[3:] for arg in argv if arg.startswith("of=")]
    if cmd == "truncate":
        targets += [arg for arg in argv[1:] if not arg.startswith("-")]
    if cmd in ("rm", "unlink", "shred"):
        targets += [arg for arg in argv[1:] if not arg.startswith("-")]
    low = argv[0].lower()
    if ps and (low in _PS_WRITE_CMDLETS or low in _PS_COPY_CMDLETS):
        targets += _ps_write_targets(argv)
    if cmd in ("cp", "mv", "install"):
        dest, sources = _copy_move_operands(argv)
        if dest:
            targets.append(dest)
        if cmd == "mv":
            targets += sources
    return targets
```

- [ ] **Step 2: Rerun the focused tests and verify the green state**

```powershell
$env:PYTHONDONTWRITEBYTECODE = "1"
python -m unittest discover -s tools/agent-coordination/tests `
  -p "test_coordination.py" -k coreutils -v
```

Expected: all three new test methods pass, including every command/suffix subtest and every preserved boundary.

- [ ] **Step 3: Run the complete repository-proportioned verification**

```powershell
sonar analyze secrets .
$env:PYTHONDONTWRITEBYTECODE = "1"
python -m unittest discover -s tools/agent-coordination/tests
python -m unittest discover -s tools/agent-assets/tests
dotnet test -c Release
python tools/checks/docs_checks.py --root . --baseline origin/main
git diff --check
git diff --name-only
```

Expected:

- both Python suites end with `OK`;
- the .NET solution builds and tests with no failures;
- `docs_checks: clean`;
- `git diff --check` emits nothing; and
- `git diff --name-only` prints exactly the two files named in this task.

- [ ] **Step 4: Inspect and commit the tested fix**

```powershell
git diff -- tools/agent-coordination/coordination.py `
  tools/agent-coordination/tests/test_coordination.py
git add -- tools/agent-coordination/coordination.py `
  tools/agent-coordination/tests/test_coordination.py
git diff --cached --check
git commit -m "fix(coordination): normalize coreutils write commands (#81)"
```

Expected: the production diff adds one local normalization and redirects only the existing coreutils comparisons; the test diff adds the matrix, boundary, and end-to-end methods.

### Task 3: Publish, review, merge, and close issue #81

**Files:**
- No additional repository files.
- GitHub state: one pull request and issue #81.

**Interfaces:**
- Consumes: the verified fix commit from Task 2.
- Produces: merged coordination hardening and a closed issue #81.

- [ ] **Step 1: Push the branch and open a ready pull request**

```powershell
git push -u origin codex/issue-81-coreutils-normalization
$body = @'
## Summary
- normalize trailing dots and spaces for coreutils write-target matching
- preserve case, path, operand-order, and PowerShell alias boundaries
- cover every affected matcher plus representative main-denial paths

## Verification
- focused red-green `unittest -k coreutils` cycle
- full coordination and asset-sync Python suites
- `dotnet test -c Release`
- `python tools/checks/docs_checks.py --root . --baseline origin/main`

Closes #81
'@
gh pr create --repo 0bnoxide/AutoGIS-Civil3D --base main `
  --head codex/issue-81-coreutils-normalization `
  --title "fix(coordination): normalize coreutils write commands (#81)" `
  --body $body
```

Expected: GitHub returns the URL of a non-draft pull request whose changed files are exactly the coordination module and its test module.

- [ ] **Step 2: Obtain adversarial review and green checks**

Request an independent review that specifically checks:

- every issue #81 matcher uses `cmd`;
- `low` still derives from raw `argv[0]`;
- `mv` still reports destination before sources;
- uppercase, pathed, and suffixed PowerShell boundary tests are meaningful; and
- the red-green evidence demonstrates that the new positive tests detect the old behavior.

```powershell
gh pr view --repo 0bnoxide/AutoGIS-Civil3D --json `
  number,headRefOid,reviewDecision,statusCheckRollup,files,url
```

Expected: the reviewed head has an approving independent review, every blocking check is successful, and only the two planned files are present.

- [ ] **Step 3: Merge the reviewed head and verify issue closure**

Pass the exact `headRefOid` from the preceding PR view as `expected_head_sha` to the GitHub connector's `github_merge_pull_request` operation. Use the repository's normal merge method and stop if the head moved after review.

After merge:

```powershell
$reviewed = gh pr view --repo 0bnoxide/AutoGIS-Civil3D --json `
  headRefOid,reviewDecision,statusCheckRollup | ConvertFrom-Json
$reviewedHead = $reviewed.headRefOid
gh pr view --repo 0bnoxide/AutoGIS-Civil3D --json `
  state,mergedAt,mergeCommit,url
gh issue view 81 --repo 0bnoxide/AutoGIS-Civil3D --json `
  state,closedAt,url
git fetch origin main
git merge-base --is-ancestor $reviewedHead origin/main
if ($LASTEXITCODE -ne 0) { throw "Reviewed head is not on origin/main" }
```

Expected: the pull request state is `MERGED`, issue #81 is `CLOSED`, and the reviewed head is an ancestor of `origin/main`.
