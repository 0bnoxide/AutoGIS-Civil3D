# AutoGIS-Civil3D Roadmap

This file is the authoritative record of capabilities, sequence, gate state, and gate changes, per the accepted [repository collaboration architecture](superpowers/specs/2026-08-02-repository-collaboration-architecture-design.md). It is not a task tracker: executable work and its live status belong to GitHub issues and pull requests, decisions to `docs/adr/`, and approved designs to `docs/superpowers/specs/`.

## Status vocabulary

`Identified` — named in the capability map; no implementation authority.
`Authorized` — the owner has explicitly opened the phase; work may be claimed.
`In Progress` — authorized work is underway on a branch or PR.
`Blocked` — authorized work cannot proceed; the blocker is recorded on the work item.
`Accepted` — the phase's exit gate has been met and the owner has accepted the evidence.
`Deferred` — explicitly postponed by owner decision.

Only an explicit owner decision may authorize, advance, reorder, or reopen a phase. Every such decision is appended to the gate-change log below. A change to phase ordering or architecture also requires an ADR.

## Capability level

| Phase | Capability | Exit-gate outcome | Status |
|---|---|---|---|
| 0 | Repository and collaboration foundation | Governance, agent tooling, local protection, CI, diagnostics preservation plan, and GitHub workflow established | Identified |
| 1 | Language-neutral handoff contract v1 | ZIP shape, JSON Schema, LandXML rules, safety limits, issue-code policy, and contract fixtures approved | In Progress |
| 2 | Pure .NET 8 validator and CLI | Restore, build, and tests pass without Autodesk; deterministic valid and invalid fixtures prove the contract | In Progress |
| 3 | AutoGIS producer adoption | AutoGIS emits conforming packages and passes cross-repository compatibility checks | Identified |
| 4 | Autodesk adapter foundation | Adapter seam approved; .NET Windows targeting and AutoCAD/Civil 3D SDK discovery established | Identified |
| 5 | Read-only Civil 3D integration | A contract-valid package can be inspected or imported without unapproved drawing mutation, with live evidence | Identified |
| 6 | Controlled Civil 3D automation | Authorized writes have explicit transaction, rollback, idempotency, and audit behavior | Identified |
| 7 | Packaging and compatibility | Supported Civil 3D versions, bundle packaging, installation, security, and upgrades are validated | Identified |
| 8 | Operational qualification and release | Authorized workstation qualification, sanitized evidence, support runbook, and release gate are complete | Identified |

Phases 1 and 2 run ahead of Phase 0 by explicit owner decision (see gate-change log, 2026-08-02). The capability sequence above is otherwise unchanged; the deviation is recorded, not rewritten.

## Delivery level

Per the two-level rule, only the active phase and the immediately next phase carry delivery detail. Later phases remain closed regardless of any plan document that mentions them.

### Active: Phases 1–2 — handoff contract v1 and pure validator (PR #3)

Normative design: [`2026-08-02-landxml-handoff-contract-design.md`](superpowers/specs/2026-08-02-landxml-handoff-contract-design.md) (amended during PR #3 review). Plan: [`2026-08-02-landxml-handoff-contract.md`](superpowers/plans/2026-08-02-landxml-handoff-contract.md).

Delivered on the PR branch: contract v1 schema, validator library, CLI with stable exit codes, 42-package golden corpus with byte-for-byte regeneration checks, diagnostics preservation with recorded hashes, CI. Remaining before the gates close:

- Reconcile the 2026-08-04 duplicate-work collision (Codex rebase; recorded on PR #3).
- Codex re-review of the review-fix delta; owner acceptance and merge.
- Gate note: contract-valid is not Civil 3D import-tested; the live import gate belongs to Phase 5.

### Next: Phase 0 — repository and collaboration foundation

The architecture spec is accepted (PR #1, merged `59cf551`). Authorization requires an owner decision plus an approved implementation plan. Scope on authorization, per the accepted split: blocking core (stateless `main` protection, worktree lifecycle, explicit-release claims with integrity floor, `init`/`doctor`, deterministic agent-asset sync, focused CI proofs) with deferred hardening on demonstrated need. The 2026-08-04 collision on PR #3 is the first demonstrated need for the claims mechanism and should weight Phase 0 scheduling.

## Parking lot

Identified capabilities with no implementation authority and no sequence: alignments, profiles, corridors, pipe networks, multiple surfaces per package, bidirectional exchange, machine-readable CLI output, package signing or encryption, coordinate or datum transformation. Moving any item out of the parking lot is a gate-change-log decision.

## Gate-change log

| Date | Decision | Recorded |
|---|---|---|
| 2026-08-02 | Owner directed Codex to execute the LandXML handoff contract plan ahead of Phase 0 (pre-Phase-0 authorization of the Phase 1–2 slice; does not authorize later phases) | PR #3 description |
| 2026-08-03 | Repository collaboration architecture accepted at exact head `ed22ac6`; merged as `59cf551` | PR #1 |
| 2026-08-04 | Roadmap document created; Phase 1–2 recorded as In Progress, all other phases Identified | This PR |
