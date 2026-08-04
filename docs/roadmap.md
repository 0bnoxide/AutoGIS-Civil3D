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
| 1 | Language-neutral handoff contract v1 | ZIP shape, JSON Schema, LandXML rules, safety limits, issue-code policy, and contract fixtures approved | Accepted |
| 2 | Pure .NET 8 validator and CLI | Restore, build, and tests pass without Autodesk; deterministic valid and invalid fixtures prove the contract | Accepted |
| 3 | AutoGIS producer adoption | AutoGIS emits conforming packages and passes cross-repository compatibility checks | Identified |
| 4 | Autodesk adapter foundation | Adapter seam approved; .NET Windows targeting and AutoCAD/Civil 3D SDK discovery established | Identified |
| 5 | Read-only Civil 3D integration | A contract-valid package can be inspected or imported without unapproved drawing mutation, with live evidence | Identified |
| 6 | Controlled Civil 3D automation | Authorized writes have explicit transaction, rollback, idempotency, and audit behavior | Identified |
| 7 | Packaging and compatibility | Supported Civil 3D versions, bundle packaging, installation, security, and upgrades are validated | Identified |
| 8 | Operational qualification and release | Authorized workstation qualification, sanitized evidence, support runbook, and release gate are complete | Identified |

Phases 1 and 2 ran ahead of Phase 0 by explicit owner decision (see gate-change log, 2026-08-02, and [ADR-0003](adr/0003-contract-slice-precedes-phase-0.md)) and were accepted together on 2026-08-04. The capability sequence above is otherwise unchanged; the deviation is recorded, not rewritten.

Acceptance of Phases 1 and 2 does not carry any Civil 3D claim: contract-valid is not equivalent to Civil 3D import-tested, and the live import gate belongs to Phase 5.

No phase is currently active. Phase 0 is the expected next authorization decision under ADR-0003, but authorizing it requires an explicit owner decision plus an approved implementation plan; naming it here does not authorize it.

## Delivery level

Per the two-level rule, only the active phase and the immediately next phase carry delivery detail. With Phases 1 and 2 accepted and no phase yet authorized, no phase carries delivery detail. Delivery detail returns when the owner authorizes the next phase; until then every phase sits at capability level, and later phases remain closed regardless of any plan document that mentions them.

## Parking lot

Identified capabilities with no implementation authority and no sequence: alignments, profiles, corridors, pipe networks, multiple surfaces per package, bidirectional exchange, machine-readable CLI output, package signing or encryption, coordinate or datum transformation. Moving any item out of the parking lot is a gate-change-log decision.

## Gate-change log

| Date | Decision | Recorded |
|---|---|---|
| 2026-08-02 | Owner directed Codex to execute the LandXML handoff contract plan ahead of Phase 0 (pre-Phase-0 authorization of the Phase 1–2 slice; does not authorize later phases) | PR #3 description; [ADR-0003](adr/0003-contract-slice-precedes-phase-0.md) |
| 2026-08-03 | Repository collaboration architecture accepted at exact head `ed22ac6`; merged as `59cf551` | PR #1 |
| 2026-08-04 | Roadmap document created; Phases 1–2 recorded as In Progress with Phase 1 sole integration-gate owner, all other phases Identified | PR #4, merged `134bc0f` |
| 2026-08-04 | Owner accepted and merged the Phase 1–2 slice, meeting the stated gate criterion; both phases advance to Accepted. Accepted evidence: contract v1 schema and rules, safety limits, issue-code policy, the 42-package golden fixture corpus with byte-for-byte regeneration checks, diagnostics preservation with recorded hashes, and the validator and CLI with stable exit codes, building and testing with no Autodesk or ArcGIS dependency — verified on merged `main` at 0 warnings, 0 errors, 181/181 tests. Carried forward as non-blocking: issues #5 (ZIP64 agreement fixture gap; design spec lagging `contract/v1/README.md`) and #2 (trailing blank lines) | PR #3, merged `8820d7c` |
