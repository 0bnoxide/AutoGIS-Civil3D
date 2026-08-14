# Phase 3 Exit Gate and Phase 4 Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record the owner's acceptance of Phase 3 evidence, authorize Phase 4 without authorizing its implementation, and close issue #78 when the governance change merges.

**Architecture:** Keep GitHub issue #78 as the evidence bundle and use the roadmap as the sole durable gate-state authority. One documentation-only pull request changes the two capability statuses and appends two distinct owner decisions; it does not alter compatibility code, Phase 4 design, or any later phase.

**Tech Stack:** Markdown, Python standard-library documentation checks, Git, GitHub CLI or the GitHub connector

## Global Constraints

- Follow `docs/agent-guide.md` and `docs/collaboration.md`; `main` is read-only, and the implementation must run in a claimed linked worktree based on current `origin/main`.
- Use branch `codex/phase-3-exit-gate` and worktree `.worktrees/codex+phase-3-exit-gate`; claim both plus `docs/roadmap.md` before editing.
- Before reading any repository file, run `sonar analyze secrets <path>` and stop if the scanner reports a secret.
- Modify only `docs/roadmap.md` in the implementation pull request.
- Change Phase 3 from `Authorized` to `Accepted` and Phase 4 from `Identified` to `Authorized`; leave every other capability status unchanged.
- Append exactly two gate-change rows in decision order and leave all existing rows byte-for-byte unchanged.
- Phase 4 authorization permits design and planning, but implementation remains blocked until a separate Phase 4 design and implementation plan are approved.
- Do not add a delivery-level link because no Phase 4 governing design exists.
- Do not change the Phase 3 workflow, compatibility scripts, fixtures, AutoGIS pin, design records, or implementation plans.
- Reproduce evidence checks before citing their results; do not copy unverified counts, hashes, or conclusions into the pull request.
- Use an independent light adversarial review for the governance-only change and merge only with green blocking checks at the reviewed head.

---

### Task 1: Record the two owner decisions

**Files:**
- Modify: `docs/roadmap.md:21-22`
- Modify: `docs/roadmap.md:57` (append after this row; never edit it)
- Reference only: `docs/superpowers/specs/2026-08-13-phase-3-exit-gate-phase-4-authorization-design.md`

**Interfaces:**
- Consumes: issue #78 as the Phase 3 evidence bundle; owner-approved gate-transition design dated 2026-08-13.
- Produces: roadmap capability states `Phase 3 = Accepted` and `Phase 4 = Authorized`, plus two append-only provenance rows.

- [ ] **Step 1: Revalidate the immutable acceptance evidence**

Run these read-only checks before editing the roadmap:

```powershell
gh run view 31672784823 --repo 0bnoxide/AutoGIS-Civil3D `
  --json headSha,status,conclusion,jobs,url
gh pr view 84 --repo 0bnoxide/AutoGIS-Civil3D `
  --json state,mergedAt,mergeCommit,statusCheckRollup,url
gh pr view 480 --repo 0bnoxide/AutoGIS `
  --json state,mergedAt,mergeCommit,statusCheckRollup,url
gh issue view 78 --repo 0bnoxide/AutoGIS-Civil3D `
  --json state,body,url
```

Expected:

- workflow run `31672784823` has conclusion `success` at head SHA `8d0af72a10f38f5a1a67c10d053f10dab8073c9e`;
- AutoGIS-Civil3D PR #84 is merged as `8d0af72a10f38f5a1a67c10d053f10dab8073c9e`;
- AutoGIS PR #480 is merged as `07a8deeac5560440b2f0b05ca8087b8b05a1fac9` with successful checks; and
- issue #78 is open and its evidence checklist is complete.

Stop if any immutable identifier disagrees with those values. A changed result is evidence drift, not permission to rewrite the accepted record.

- [ ] **Step 2: Change the capability states and append the decision rows**

Replace only the Phase 3 and Phase 4 capability rows with:

```markdown
| 3 | AutoGIS producer adoption | AutoGIS emits conforming packages and passes cross-repository compatibility checks | Accepted |
| 4 | Autodesk adapter foundation | Adapter seam approved; .NET Windows targeting and AutoCAD/Civil 3D SDK discovery established | Authorized |
```

Append these exact rows at the bottom of the gate-change log, after the 2026-08-10 Phase 3 authorization row:

```markdown
| 2026-08-13 | Owner accepted the Phase 3 exit-gate evidence collected on issue #78; the phase advances to Accepted. The accepted evidence remains the bundle linked from issue #78. Authorizes no later phase | Issue #78; [approved gate-transition design](superpowers/specs/2026-08-13-phase-3-exit-gate-phase-4-authorization-design.md) |
| 2026-08-13 | Owner authorized Phase 4. The phase advances to Authorized and becomes the active integration gate; implementation remains blocked until a Phase 4 design and plan under `docs/superpowers/` are approved. Authorizes no later phase | [Approved gate-transition design](superpowers/specs/2026-08-13-phase-3-exit-gate-phase-4-authorization-design.md) |
```

Do not add a Phase 4 delivery-level paragraph or link. The approved gate-transition design expressly leaves that design work for Phase 4.

- [ ] **Step 3: Run the documentation checks against the branch baseline**

```powershell
sonar analyze secrets docs
sonar analyze secrets tools/checks
$env:PYTHONDONTWRITEBYTECODE = "1"
python tools/checks/docs_checks.py --root . --baseline origin/main
python -m unittest discover -s tools/checks/tests -v
git diff --check
git diff --name-only
```

Expected:

- `docs_checks: clean`;
- the documentation-check test suite ends with `OK`;
- `git diff --check` emits nothing; and
- `git diff --name-only` prints only `docs/roadmap.md`.

- [ ] **Step 4: Inspect the governance diff and commit it**

```powershell
git diff -- docs/roadmap.md
git add -- docs/roadmap.md
git diff --cached --check
git commit -m "docs(roadmap): accept phase 3 and authorize phase 4 (#78)"
```

Expected: the commit contains two status-cell changes and two appended log rows, with no edits to earlier log rows.

### Task 2: Publish, review, merge, and close issue #78

**Files:**
- No additional repository files.
- GitHub state: one pull request and issue #78.

**Interfaces:**
- Consumes: the verified governance commit from Task 1.
- Produces: merged authoritative roadmap state and a closed issue #78.

- [ ] **Step 1: Push the branch and open a ready pull request**

```powershell
git push -u origin codex/phase-3-exit-gate
$body = @'
## Summary
- accept the completed Phase 3 evidence bundle
- authorize Phase 4 while keeping implementation blocked on its own design and plan
- preserve the append-only gate history and leave later phases closed

## Verification
- `python tools/checks/docs_checks.py --root . --baseline origin/main`
- `python -m unittest discover -s tools/checks/tests -v`
- `git diff --check`

Closes #78
'@
gh pr create --repo 0bnoxide/AutoGIS-Civil3D --base main `
  --head codex/phase-3-exit-gate `
  --title "docs(roadmap): accept phase 3 and authorize phase 4 (#78)" `
  --body $body
```

Expected: GitHub returns the URL of a non-draft pull request whose only changed file is `docs/roadmap.md`.

- [ ] **Step 2: Obtain the required review and green checks**

Request one independent light adversarial review. The reviewer must verify:

- the Phase 3 evidence identifiers match Step 1 of Task 1;
- Phase 4 implementation remains blocked;
- no later phase is authorized;
- old gate-log rows are unchanged; and
- issue #78 closes only through merge.

```powershell
gh pr view --repo 0bnoxide/AutoGIS-Civil3D --json `
  number,headRefOid,reviewDecision,statusCheckRollup,files,url
```

Expected: the reviewed head has an approving review, every blocking check is successful, and `files` contains only `docs/roadmap.md`. The path-filtered `compat-autogis` workflow is not expected on this documentation-only pull request; run `31672784823` remains the immutable Phase 3 evidence.

- [ ] **Step 3: Merge the reviewed head and verify closure**

Read the exact `headRefOid` from the preceding PR view and pass that value as `expected_head_sha` to the GitHub connector's `github_merge_pull_request` operation. Use the repository's normal merge method; do not merge if the head changed after review.

After merge:

```powershell
$reviewed = gh pr view --repo 0bnoxide/AutoGIS-Civil3D --json `
  headRefOid,reviewDecision,statusCheckRollup | ConvertFrom-Json
$reviewedHead = $reviewed.headRefOid
gh pr view --repo 0bnoxide/AutoGIS-Civil3D --json `
  state,mergedAt,mergeCommit,url
gh issue view 78 --repo 0bnoxide/AutoGIS-Civil3D --json `
  state,closedAt,url
git fetch origin main
git merge-base --is-ancestor $reviewedHead origin/main
if ($LASTEXITCODE -ne 0) { throw "Reviewed head is not on origin/main" }
```

Expected: the pull request state is `MERGED`, issue #78 is `CLOSED`, and the reviewed head is an ancestor of `origin/main`.
