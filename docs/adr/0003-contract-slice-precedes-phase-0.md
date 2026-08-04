# ADR-0003: Execute the Phase 1–2 contract slice before Phase 0

**State:** Accepted (explicit owner decision, 2026-08-02, recorded in the PR #3 description)

**Date:** 2026-08-02 (decision); recorded 2026-08-04

## Context

The accepted collaboration architecture sequences Phase 0 (repository and collaboration foundation) first, then owner authorization of the contract phase. On 2026-08-02 the owner directed Codex to execute the approved LandXML handoff contract plan immediately, ahead of Phase 0. The architecture requires that a phase-order change receive an ADR in addition to its gate-change-log entry; this ADR records that decision.

## Decision

The Phase 1–2 slice (handoff contract v1 and the pure .NET validator/CLI, delivered on PR #3) executes before Phase 0. The deviation is bounded to this slice: it does not authorize any later phase, and Phase 0 remains the next authorization decision after the slice is accepted. Phase 1 owns the integration gate; Phase 2 is authorized by the same decision and is gated after Phase 1 acceptance.

## Alternatives

Establish Phase 0 first as sequenced (rejected by the owner in favor of contract momentum); treat the contract work as unsequenced pre-phase work (rejected: it is exactly the Phase 1–2 scope and must carry those gates).

## Consequences

The slice ran without the Phase 0 claims mechanism, and the risk materialized: the 2026-08-04 duplicate-work collision on PR #3, where both agents implemented the same review fixes. That collision is the first demonstrated need for the claims mechanism and weights Phase 0 scheduling.
