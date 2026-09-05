# ADR-0008: Develop and qualify New Proposal on Civil 3D 2026

**State:** Accepted (owner decision recorded in the accompanying
[roadmap gate-change-log](../roadmap.md#gate-change-log) row)

**Date:** 2026-09-04

## Context

The adapter-foundation design and [ADR-0007](0007-civil3d-2025-reference-sourcing.md)
selected Civil 3D 2025 using the preserved diagnostic evidence. The owner
subsequently directed: "We'll just switch development to utilize 2026 civil3d".
This replaces the development target. The owner subsequently approved the
installed 2026 SDK as the reference source and a dedicated Windows CI runner;
neither authorization qualifies a new runtime.

## Decision

Civil 3D 2026 is the single Phase 4 development and native-qualification
target for `New Proposal`. Its AutoCAD release series is `R25.1` and its
Civil API series is `13.8`. Cross-release reference and host checks must
fail closed. `AecBaseMgd` has the independent `8.8` version series; its
provenance must match the selected Civil 3D release rather than the AutoCAD
numbering.
Preserve `net8.0-windows`, x64, the single adapter project, and non-copying
Autodesk references.

Compile the adapter against the installed Civil 3D 2026 SDK. The normal
Windows root is `C:\Program Files\Autodesk\AutoCAD 2026`; one overridable
install-root property supplies the five assembly paths, while the existing
per-assembly path overrides remain available only for controlled negative
probes. All Autodesk references remain `Private=false`. The Autodesk NuGet
references and their central pins are removed; locked restore remains in
force for the repository's remaining package dependencies.

The adapter build runs on a dedicated Windows CI runner with Civil 3D 2026
installed. General PR jobs do not use the daily development workstation.

This overrides the 2025 target and release-specific pins and series in
ADR-0007 and the adapter-foundation design, including ADR-0006's retention
of that target. It also overrides the foundation's exclusion of 2026
targeting and its deferral of a 2026 build to Phase 7. Multi-release support,
packaging, and compatibility work remain subject to their existing gates.

The installed SDK location and assembly identities establish build
provenance. Never substitute 2025 assemblies or weaken series checks. A
future source or CI-policy change requires its own explicit owner decision.

## Alternatives

- Retaining 2025 as the active target conflicts with the owner's decision.
- Adding 2026 alongside 2025 introduces multi-release support the owner did
  not request.
- Keeping the NuGet references would preserve a source the owner replaced.

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
