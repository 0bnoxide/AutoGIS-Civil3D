# Phase 0 Implementation Plan: Repository and Collaboration Foundation

**Date:** 2026-08-04

**Status:** Proposed — requires owner approval before implementation may be claimed

**Governing design:** [`2026-08-02-repository-collaboration-architecture-design.md`](../specs/2026-08-02-repository-collaboration-architecture-design.md), accepted at `ed22ac6`, merged `59cf551`. Decisions: [ADR-0002](../../adr/0002-agent-collaboration-and-main-protection.md), [ADR-0004](../../adr/0004-one-adversarial-review-proportioned-to-risk.md).

**Scope:** The blocking core only. Deferred hardening is out of scope and is not stubbed.

**Tech stack:** Python 3, standard library only. No third-party packages, no .NET project reference, no product dependency. PowerShell, Claude, Codex, and Git integrations are thin adapters over one Python rule engine.

## Sequencing rationale

Steps 1–4 deliver main protection and claims, which is the priority: the 2026-08-04 duplicate-work collision on PR #3 happened because no claim existed, and local `main` is currently unprotected because the account plan has no server-side branch protection. Everything after step 4 is necessary for Phase 0 acceptance but does not gate day-to-day safety.

Each step is one commit or a small group, independently verifiable, and leaves the repository working.

## Global constraints

- Never auto-expire or reap a claim. `doctor` marks stale-suspect; only `release --force <id> --reason ...` clears an orphan, and that is owner-authorized.
- The main-protection rule is stateless and evaluated independently of the registry. A corrupt, missing, or locked registry must never turn a `main` write into an allow. This is a test, not a comment.
- Repair preserves damaged state. Nothing in this plan deletes claim state; it quarantines.
- The coordination module is a guardrail, not a security boundary. Repository permissions and GitHub remain the external authority.
- Untracked diagnostic artifacts in any working tree are evidence: never silently staged, moved, or deleted.

## Step 1: Module skeleton, discovery, and exit contract

Create `tools/agent-coordination/` with the Python package and a single entry point exposing `init`, `doctor`, `status`, `claim`, `release`, `check`, `sync-main`. Deferred commands (`resync`, temporal claims, break-glass) are absent, not stubbed.

Implement repository discovery: resolve the repository root and the primary working tree through `git rev-parse --git-common-dir`, so a linked worktree resolves to the same registry location as the primary tree. Resolve branch, worktree, and target paths with symlink and reparse-point resolution before any comparison.

Define the exit contract now, because every adapter depends on it: `0` allow or success, `1` deny by policy, `2` misuse or bad invocation, `3` operational failure (unreadable registry, missing Git). Document it in the module README.

**Acceptance:** running each command from the primary tree and from a linked worktree resolves the same repository root and registry path. Exit codes are asserted by unit tests.

## Step 2: Stateless main protection and `check`

Implement the rule that decides whether a resolved target is a write to local `main`, independent of any registry read. Implement `check`, which confirms the current branch, worktree, and intended target are inside the approved scope, and is the call every adapter makes before a write-producing operation.

The rule evaluates the resolved branch and the resolved target path. It denies commits on `main`, writes whose resolved target is inside the primary tree while `main` is checked out, and refspecs targeting remote `main`.

**Acceptance:** a decision-matrix test covers branch-on-main, branch-off-main, symlinked target, reparse-point target, and relative-path escape. A separate test deletes, truncates, and corrupts the registry, then asserts every `main` write is still denied — the stateless guarantee. Deny is exit `1` with a human-readable reason naming the resolved target.

## Step 3: Git hook adapters and `init`

Add `.githooks/pre-commit` and `.githooks/pre-push` as thin adapters that normalize their payload and call the Python rule engine. `pre-commit` blocks commits on `main`. `pre-push` parses the refspec and blocks pushes targeting remote `main`, including the forms that name the remote ref explicitly.

`init` verifies the target and sets `core.hooksPath=.githooks`. It reports what it changed and is idempotent.

**Acceptance:** in a disposable repository, a commit on `main` and a direct push to remote `main` are both denied by the real hooks, and the same operations succeed on a feature branch. `init` run twice produces no second change.

## Step 4: Claim registry, `claim`, `release`, `status`

`.agent-state/claims.json` at the primary working tree resolved through `git rev-parse --git-common-dir`. A claim records session identity, harness, process and host where available, claim kind, branch, worktree, file glob, and start time.

Registry mutation takes an OS-level writer lock, rereads after acquiring the lock, writes a temporary sibling, and atomically replaces the registry. A contested live resource is rejected with the conflicting claim identified.

`release` releases by id. `release --force <id> --reason ...` is the only orphan recovery and records the reason. `status` prints live claims.

Corrupt or inaccessible claim state blocks new claims and claim-dependent writes with a repair instruction, and exits `3` — never `0`.

**Acceptance:** a decision-matrix test over claim kinds and overlaps. A focused contested-claim integration test: two processes race the same resource, exactly one wins, the registry is valid afterward, and the loser's message names the winner. A crash-during-write test asserts the registry is never left partially written.

## Step 5: ADR number allocation

Allocating an ADR number is structurally a claim: parallel sessions must not take the same one, which has already happened. Rather than a separate sequence script, add `claim adr` — it allocates the next unused number atomically under the same lock and records the allocation, so a number is assigned like a ticket and never reissued.

**Acceptance:** concurrent allocation requests return distinct numbers. An allocation that is never used leaves a gap, and a gap is not reissued. Until this ships, the interim convention in `docs/adr/README.md` applies.

## Step 6: `doctor`

Detect and report: missing or displaced Git hooks, wrong worktree placement, stale-suspect claims, agent-asset drift, branch and upstream errors, and unavailable optional tools. Codex project-hook trust is reported as unverified until the documented `/hooks` inspection and activation probe succeed; it is never inferred from file presence.

`doctor` reports. It does not repair, and it does not expire claims.

**Acceptance:** each condition is provoked in a fixture and the corresponding report asserted. A stale-suspect claim is reported and still present afterward.

## Step 7: `sync-main`

The only ordinary mutation permitted on local `main`: requires a clean tree, fast-forward only, from `origin/main`. Refuses on a dirty tree or a non-fast-forward with a clear message.

**Acceptance:** fast-forward succeeds; dirty tree and diverged history both refuse without mutating.

## Step 8: Harness adapters

Claude and Codex pre-tool adapters that normalize their respective payloads and call the same rule engine. They add no policy of their own. Codex hooks are configured through checked-in `.codex/hooks.json`; `.codex/hooks/` holds only referenced handler scripts and is not a discovery surface.

**Acceptance:** a synthetic main-targeting payload in each harness's format is denied through its adapter, asserted in CI. Adapter parity is limited to this: exhaustive parity is deferred hardening.

## Step 9: Agent assets and deterministic sync

Pinned canonical sources under `tools/agent-assets/skills/`, rendered deterministically into `.agents/skills/` and `.claude/skills/`. The sync recreates destinations cleanly, prunes destination-only files, and supports a check-only mode for CI.

**Acceptance:** sync twice is byte-identical. A destination-only file is pruned. Check-only mode fails on drift without writing.

## Step 10: Canonical guidance documents

`docs/agent-guide.md` is canonical; `AGENTS.md` and `CLAUDE.md` are thin entrypoints with startup details and links only. Add `CONTRIBUTING.md` and `docs/collaboration.md` for contribution and checkout procedure.

The guide records the operating rules that currently live only in review history:

- The checkout and worktree lifecycle, including `check` before each write-producing operation.
- The merge bar and review tiers from ADR-0004, and the review triggers that actually reach a reviewer.
- Any bug or issue discovered during any work item is opened as a GitHub issue and tracked.
- Prefer the codebase-memory MCP server or a search subagent over manual file-by-file grep when locating code, and treat its index as advisory navigation only — never as an authoritative record. Re-index after a significant merge; the index was found stale at 108 nodes after the Phase 1–2 merge.

**Acceptance:** no policy statement appears in two places with different wording. `AGENTS.md` and `CLAUDE.md` contain no rule not present in the guide.

## Step 11: CI

Windows, no Autodesk, ArcGIS, or Civil 3D. Blocking checks: synthetic main-targeting payload denial through both adapters; real Git-hook denial of commits and direct pushes to remote `main` in a disposable repository; registry decision-matrix and contested-claim tests; agent-asset check-only mode.

**Acceptance:** CI fails when protection is removed. Verify by deliberately breaking each guarantee on a scratch branch and confirming the corresponding job fails.

## Step 12: Harness interception smoke test

CI cannot prove a harness actually invokes its project hook before an edit, so this runs outside CI. Each real harness loads the checked-in project configuration and attempts a harmless sentinel edit targeting `main` in a disposable repository. It passes only when the harness denies the action before filesystem mutation. Evidence is recorded on the Phase 0 PR and includes Codex `/hooks` trust inspection. Repeat after any hook-configuration change.

The real primary worktree is never the mutation target.

**Acceptance:** both harnesses deny before mutation, with evidence recorded on the PR.

## Exit gate

Phase 0 is complete when the acceptance criteria in the governing design are met. That list is authoritative and is not restated here.

## Out of scope

Temporal claims, heartbeat, TTL, automatic expiry and reaping, `resync` automation, a general coded break-glass flow, broad corruption-repair automation, exhaustive adapter parity, and broader CI matrices. These join the interface only after a demonstrated failure shows the explicit-release plus `doctor` model is insufficient.

Any future temporal-claim design is bound by one invariant from the accepted architecture: a claim is never released or ignored solely because a heartbeat was missed or a TTL elapsed. Takeover requires explicit release, or positive owner-liveness confirmation plus a recorded takeover handshake.
