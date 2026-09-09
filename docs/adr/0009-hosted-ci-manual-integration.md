# ADR-0009: Build the 2026 preview in hosted CI and qualify it manually

**State:** Accepted (owner decision recorded in the accompanying
[roadmap gate-change-log](../roadmap.md#gate-change-log) row)

**Date:** 2026-09-08

## Context

[ADR-0008](0008-civil3d-2026-development-target.md) selected the installed
Civil 3D 2026 SDK and a dedicated Windows runner for the New Proposal preview.
That made ordinary hosted CI unable to build the adapter and tied delivery to
machine provisioning. The owner amended the policy so hosted CI builds and
tests every push while licensed-host integration remains manual.

## Decision

Civil 3D 2026 remains the sole target: AutoCAD 25.1, Civil 13.8, x64. Compile
the AutoCAD surface against Autodesk's `AutoCAD.NET`, `AutoCAD.NET.Core`, and
`AutoCAD.NET.Model` packages pinned to 25.1.0. Exclude their runtime assets and
refuse missing, misnamed, or wrong-series references. Resolve the current
Civil assembly only inside the guarded host check by loading
`Autodesk.Civil.ApplicationServices.CivilDocument, AeccDbMgd` and reading its
assembly version. This metadata lookup is not a general dynamic Civil API and
does not establish a reference source for future Civil operations.

The ordinary hosted Windows CI restores in locked mode, builds and tests the
actual adapter for the 2026 matrix entry, then uploads a commit-specific
preview artifact. The artifact contains only the adapter and Proposal DLLs,
the smoke script, the manual instructions, and build metadata with the exact
commit and both product-DLL SHA-256 values. Autodesk runtime binaries, debug
files, dependency manifests, machine paths, templates, and user data are not
distributed.

Native evidence is gathered manually on the separately licensed Civil 3D 2026
work computer after `NETLOAD`. `AUTOGISPROPOSALSMOKE` reports command/host
binding and observed versions without modifying a drawing; the existing
`AUTOGISNEWPROPOSAL` command opens the preview for inspection and cancellation.
A smoke pass establishes only binding. It does not qualify creation, templates,
or the complete workflow.

A build-only preview may merge after the repository's independent review and
hosted checks pass. Manual preview evidence remains required independently,
and full Phase 4 acceptance still requires the complete native workflow and
the owner's approved-template qualification.

## Alternatives

- Keeping installed SDK references and a dedicated runner would retain the
  hosted-CI delivery blocker and unnecessary runner administration.
- Uploading installed Autodesk assemblies would distribute runtime files that
  are not product artifacts.
- Treating a hosted build or guard-only smoke as native qualification would
  overstate the evidence.

## Consequences

ADR-0008 continues to govern the single 2026 target and its fail-closed series
checks. This ADR supersedes only its installed-SDK reference source and
dedicated-runner requirements. Multi-release support, creation, native API
sourcing for later operations, complete workflow qualification, and Phase 4
acceptance remain unchanged.

The approved [hosted-CI/manual-integration design](../superpowers/specs/2026-09-08-hosted-ci-manual-integration-design.md)
and [implementation plan](../superpowers/plans/2026-09-08-hosted-ci-manual-integration.md)
own the detailed handoff and verification requirements.
