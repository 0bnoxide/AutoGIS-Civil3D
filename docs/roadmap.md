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

Phases 1 and 2 run ahead of Phase 0 by explicit owner decision (see gate-change log, 2026-08-02, and [ADR-0003](adr/0003-contract-slice-precedes-phase-0.md)). Both are factually in progress on PR #3; integration-gate ownership is single and separate from work status: Phase 1 owns the gate, and Phase 2's gate is evaluated only after Phase 1 acceptance. The capability sequence above is otherwise unchanged; the deviation is recorded, not rewritten.

## Delivery level

Per the two-level rule, only the active phase and the immediately next phase carry delivery detail: Phase 1 (active, integration-gate owner) and Phase 2 (next, gated after Phase 1 acceptance). All other phases — including Phase 0, the next authorization decision after this slice — remain at capability level, and later phases remain closed regardless of any plan document that mentions them.

### Active: Phase 1 — handoff contract v1 (PR #3)

Normative design: [`2026-08-02-landxml-handoff-contract-design.md`](superpowers/specs/2026-08-02-landxml-handoff-contract-design.md). An amendment (physical container consistency; root-cause issue reduction) is proposed on PR #3 and is not part of this branch or `main` until that PR merges. Plan: [`2026-08-02-landxml-handoff-contract.md`](superpowers/plans/2026-08-02-landxml-handoff-contract.md).

Delivered on the PR branch for this phase: contract v1 ZIP shape and JSON Schema, LandXML surface rules, safety limits, issue-code policy, and the 42-package golden fixture corpus with byte-for-byte regeneration checks, plus diagnostics preservation with recorded hashes.

Durable gate criteria remaining: owner acceptance and merge of PR #3. Live task state — including the 2026-08-04 duplicate-work reconciliation and re-review — is tracked on PR #3, not here. Gate note: contract-valid is not Civil 3D import-tested; the live import gate belongs to Phase 5.

### Next: Phase 2 — pure .NET 8 validator and CLI (PR #3)

Authorized by the same 2026-08-02 owner decision and implemented on the same PR as Phase 1 ([ADR-0003](adr/0003-contract-slice-precedes-phase-0.md)). Delivered on the PR branch for this phase: the validator library, CLI with stable exit codes, and CI, all restoring, building, and testing without any Autodesk dependency; the golden corpus doubles as this phase's deterministic fixture evidence. The gate is evaluated against that evidence immediately after Phase 1 acceptance; until then Phase 1 owns the integration gate.

## Parking lot

Identified capabilities with no implementation authority and no sequence: alignments, profiles, corridors, pipe networks, multiple surfaces per package, bidirectional exchange, machine-readable CLI output, package signing or encryption, coordinate or datum transformation. Moving any item out of the parking lot is a gate-change-log decision.

## Gate-change log

| Date | Decision | Recorded |
|---|---|---|
| 2026-08-02 | Owner directed Codex to execute the LandXML handoff contract plan ahead of Phase 0 (pre-Phase-0 authorization of the Phase 1–2 slice; does not authorize later phases) | PR #3 description; [ADR-0003](adr/0003-contract-slice-precedes-phase-0.md) |
| 2026-08-03 | Repository collaboration architecture accepted at exact head `ed22ac6`; merged as `59cf551` | PR #1 |
| 2026-08-04 | Roadmap document created; Phases 1–2 recorded as In Progress with Phase 1 sole integration-gate owner, all other phases Identified | This PR |
