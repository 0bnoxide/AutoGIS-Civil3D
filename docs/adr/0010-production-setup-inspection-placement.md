# ADR-0010: Keep Survey Preflight and Audit EG Surface in Phase 4 production setup

**Status:** Accepted 2026-09-17 by owner instruction: "Resolve issue 121,
we'll treat as Survey Preflight and Audit EG as part of prod setup".

## Context

The approved [Civil Production Accelerator design](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md)
places these workflows in the production-foundation sequence. The
[roadmap](../roadmap.md)'s broad Phase 5 description could also be read as
covering them because both inspect without changing drawings.
[Issue #121](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/121) asked the
owner to settle that ambiguity.

## Decision

`Survey Preflight` and `Audit EG Surface` remain in Phase 4 production
setup. Survey Preflight checks inputs before an existing-ground surface is
built; Audit EG Surface checks the surface supplied for production. Their
role in setup determines phase placement, not whether they write a drawing.
Phase 5's inspection and design-assistance scope is narrowed accordingly in
the roadmap.

The approved workflow sequence and existing `New Proposal` exit gate remain
unchanged. Phase 4 remains Authorized and Phase 5 remains Identified. This
placement decision does not approve the [candidate workflow designs](../superpowers/specs/2026-09-11-follow-on-workflow-candidates-design.md),
their proposed reordering, or their implementation plans. Each follow-on
workflow still needs its own approved design and implementation plan before
code is written; this decision provides no implementation or native
qualification evidence.

## Alternative considered

Move both workflows to Phase 5 because they are read-only. Rejected by the
owner's production-setup decision; it would change the approved sequence
based on the access mode rather than the workflow's purpose.

## Consequences

The roadmap records the explicit boundary and owner decision, while the
governing design retains its sequence. The candidate design links to this
decision instead of treating phase placement as unanswered. No later phase
is authorized.
