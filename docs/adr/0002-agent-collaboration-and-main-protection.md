# ADR-0002: Claude/Codex collaboration, neutral agent structure, and local-first `main` protection

**State:** Accepted (owner acceptance of the collaboration architecture, PR #1, 2026-08-03; merged `59cf551`)

**Date:** 2026-08-02

## Context

Claude and Codex develop this repository as peers through one GitHub identity, without server-side branch protection under the current account plan. Ungoverned, that invites direct writes to `main`, branch collisions, and duplicated harness-specific policy.

## Decision

Each work item assigns one writer and one independent reviewer; roles rotate and hand off explicitly, and review targets the exact pushed head. Coordination machinery is one deep module under `tools/agent-coordination/` with a small interface (`init`/`doctor`/`status`/`claim`/`release`/`check`/`sync-main`); harnesses and Git hooks are adapters at that seam. Repository guidance is neutral and canonical in `docs/agent-guide.md`, with `AGENTS.md` and `CLAUDE.md` as thin entrypoints; pinned agent assets render deterministically from `tools/agent-assets/`. Local `main` is made read-only for both agents and ordinary Git commands before server-side protection exists. GitHub and committed files remain authoritative.

## Alternatives

Direct commits to `main` with post-hoc review (rejected: no independent gate); a hosted coordinator or daemon (rejected: a security boundary and operational surface this repository does not need); duplicating policy per harness (rejected: guaranteed drift).

## Consequences

All work lands through reviewed PRs from isolated worktrees. Until the Phase 0 claims tooling exists, collision avoidance is manual — a limitation demonstrated by the 2026-08-04 duplicate-work collision on PR #3. Full mechanism and scope split (blocking core vs deferred hardening): [collaboration architecture](../superpowers/specs/2026-08-02-repository-collaboration-architecture-design.md).
