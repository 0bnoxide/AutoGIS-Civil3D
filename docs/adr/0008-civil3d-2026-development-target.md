# ADR-0008: Develop and qualify New Proposal on Civil 3D 2026

**State:** Accepted (owner decision recorded in the accompanying
[roadmap gate-change-log](../roadmap.md#gate-change-log) row)

**Date:** 2026-09-04

## Context

The adapter-foundation design and [ADR-0007](0007-civil3d-2025-reference-sourcing.md)
selected Civil 3D 2025 using the preserved diagnostic evidence. The owner
subsequently directed: "We'll just switch development to utilize 2026 civil3d".
This replaces the development target; it does not qualify a new runtime or
approve a different reference source.

## Decision

Civil 3D 2026 is the single Phase 4 development and native-qualification
target for `New Proposal`. Its AutoCAD release series is `R25.1` and its
Civil API series is `13.8`. Cross-release reference and host checks must
fail closed. `AecBaseMgd` has an independent version series; its provenance
must match the selected Civil 3D release rather than the AutoCAD numbering.
Preserve `net8.0-windows`, x64, the single adapter project, and non-copying
Autodesk references.

This overrides the 2025 target and release-specific pins and series in
ADR-0007 and the adapter-foundation design, including ADR-0006's retention
of that target. It also overrides the foundation's exclusion of 2026
targeting and its deferral of a 2026 build to Phase 7. Multi-release support,
packaging, and compatibility work remain subject to their existing gates.

Retain pinned NuGet reference sourcing and locked restore. Resolve exact
matching package versions and record their provenance before implementing
the target change. If matching Civil 3D references are unavailable, the
dependent adapter build and delivery are blocked for an owner sourcing
decision. Never substitute 2025 assemblies, weaken series checks, or adopt
installed SDK references or a different CI runner implicitly. A source or
CI-policy change requires its own explicit owner decision.

## Alternatives

- Retaining 2025 as the active target conflicts with the owner's decision.
- Adding 2026 alongside 2025 introduces multi-release support the owner did
  not request.
- Treating an installed SDK as an automatic fallback changes the accepted
  reference and CI policy without authorization.

## Consequences

Phase 4 remains Authorized; no later phase opens. The approved accelerator
design and implementation plan follow this single target. Their native
probes, real-template qualification, receipt policy, and owner acceptance
requirements remain in force. A successful build or preview is not native
qualification of the completed workflow.

The original 2025 diagnostic kit and evidence remain unchanged historical
evidence. They establish no 2026 runtime or workflow support claim. Live
reference availability and delivery blockers belong on the delivery issue,
not in this decision record.
