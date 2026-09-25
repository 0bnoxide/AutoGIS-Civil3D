# ADR-0012: Keep the M2 advisory proposal preview in Phase 4

**Status:** Accepted 2026-09-24 by the owner's approval of the recommended M2 design, written specification, and implementation plan. The separate reservation-removal gate must merge before product edits.

## Context

The merged M1 MCP facade validates handoff bundles without Autodesk. The [New Proposal design](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md) keeps planning pure, but its production milestones normally end with a live Civil 3D run and demonstrated time savings. Phase 4 remains Authorized; native New Proposal preview and qualification are incomplete. The owner requested the next MCP stage, approved its design direction, then approved the [written M2 design](../superpowers/specs/2026-09-24-phase4-m2-proposal-preview-design.md).

## Decision

Place M2 in Phase 4 as a narrow, read-only advisory interface over the existing `StandardsManifest.Parse` and `ProposalPlanner.Build`. One local stdio `preview_proposal` tool uses an operator-selected manifest snapshot and exposes only a bounded, redacted action projection. It does not inspect templates, run Civil 3D, approve execution, create a proposal, or issue a receipt.

For this Autodesk-free planning slice alone, managed tests, a real stdio protocol trace, independent review, and green checks are sufficient to merge the M2 implementation without a live Civil 3D run. This is a narrow exception to the New Proposal design's live-host-per-milestone rule. It does not meet the Phase 4 exit gate, qualify the native New Proposal preview, or authorize Phase 5 inspection or Phase 6 writes. The approved [M2 plan](../superpowers/plans/2026-09-24-phase4-m2-proposal-preview-implementation.md) still follows the roadmap's separate documentation reservation/removal sequence.

## Alternatives considered

- Put M2 in Phase 5: its pure planning advice serves Phase 4 production setup, and it needs no drawing inspection.
- Require a live Civil 3D run for this slice: it would test a host path that M2 never calls and conflate advisory planning with the still-open native New Proposal gate.
- Pass manifest bytes or a path per call: this would give callers standards selection and increase exposure of proprietary configuration. The approved design uses a capped launch-time snapshot.
- Extend M2 into Create or a native bridge: that would cross the existing approval, receipt, and qualification boundaries without an approved design.

## Consequences

M2 can provide inspectable agent-facing setup advice while leaving the adapter and M1 intact. Its output must stay bounded, allowlisted, and explicit that native preflight and template checks were not performed. Operators choose the manifest when launching the local child process and restart it to reload standards. Native preview issue [#105](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/105) and the Phase 4 acceptance gate remain open.
