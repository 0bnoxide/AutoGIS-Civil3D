# Phase 4 Autodesk Adapter Foundation — Design

**Status:** Partly superseded 2026-09-04 by
[ADR-0006](../../adr/0006-civil-production-accelerator.md) and the
[Civil Production Accelerator design](2026-09-04-civil-production-accelerator-design.md),
which governs roadmap Phase 4. [ADR-0006](../../adr/0006-civil-production-accelerator.md#consequences)
records which decisions below are superseded and which remain governing.
[ADR-0008](../../adr/0008-civil3d-2026-development-target.md) additionally
replaces the 2025 development target and its release-specific references
with the single 2026 target. [ADR-0009](../../adr/0009-hosted-ci-manual-integration.md)
then supersedes ADR-0008's installed-SDK source and dedicated-runner policy;
the approved [hosted-CI/manual-integration design](2026-09-08-hosted-ci-manual-integration-design.md)
governs preview build delivery and the separate native handoff.
The superseded scope bound also appears in **Acceptance evidence**,
**Exclusions**, and **Known ceilings**: their no-drawing-access, no-live-load,
and no-native-execution restrictions do not govern `New Proposal`. Its
drawing creation, reads needed for verification, transactions, and live
qualification follow the approved Civil Production Accelerator design.
Phase 5 inspection workflows and package import remain outside Phase 4.
The reserved paths below remain reserved.

**Original status:** Proposed 2026-09-04. Governs roadmap Phase 4 (authorized
2026-08-13, [gate-change log](../../roadmap.md)). Approval is an owner
decision recorded on the pull request that merges this document; it
approves the adapter seam and the build foundation only. Implementation
stays blocked until the separate Phase 4 implementation plan is approved
and the roadmap marker this design reserves is removed, per the
[phase-aware documentation gate](2026-08-14-phase-aware-documentation-gate-design.md).

## Problem

Phase 4's exit gate reads "Adapter seam approved; .NET Windows targeting
and AutoCAD/Civil 3D SDK discovery established." Nothing in the product
graph references Autodesk. The only Autodesk-referencing code is the
standalone [diagnostic kit](../../../diagnostics/AutoGIS.Civil3D.Diagnostics/README.md),
which is preserved evidence, resolves assemblies from an installed Civil 3D
through a PowerShell script, and is built outside the solution and outside
CI. The validator's public surface is a report with counts; it exposes no
way for a consumer to reach the surface it verified. So three things are
unestablished: which project may reference Autodesk and how it is built
where no Civil 3D is installed, which reference assemblies it compiles
against, and what a validated package hands the adapter.

The [pre-repository handoff](../../reference/2026-08-02-architecture-handoff.md)
asks that the adapter boundary be designed so that adding Civil 3D 2026 is
routine, while building and proving on 2025 first. The
[live diagnostic run](../../diagnostics/2026-08-04-live-run-civil3d-2025.md)
already proved that a DLL compiled against NuGet reference assemblies on a
machine without Civil 3D 2025 loads and binds to the genuine assemblies on
the 2025 workstation.

## Decisions

### Adapter seam

The seam is the validator's public surface, and dependency direction is the
one recorded in [ADR-0001](../../adr/0001-handoff-contract-ownership.md):
adapter → validator, never the reverse.

1. `BundleValidator.ValidateBundle` stays the only entry point. The adapter
   consumes a package only when the report's status carries no error; the
   adapter never opens the ZIP or reads `surface.landxml` through any other
   path. This keeps the contract's safety limits (bounded reads, forbidden
   sequences, entry-shape rules) in one place.
2. The validator exposes what an inspecting consumer needs and cannot
   re-derive without duplicating the parser: the manifest facts already
   parsed internally (producer name and version, linear unit, vertical
   datum status and name) join the existing verified metadata record, and a
   validated package offers read access to the verified `surface.landxml`
   bytes through the same bounded-read path the validator used. Exact
   member names and shapes belong to the implementation plan.
3. Geometry interpretation stays out of the seam. Phase 5 chooses between
   Civil 3D's own LandXML import and point/face construction through the
   Civil 3D API; both start from the verified bytes, so the seam forecloses
   neither. The internal point and surface-summary types stay internal.

Rejected alternatives: the adapter re-opening the ZIP itself (duplicates
the safety limits; the second copy drifts); a public geometry model
(builds a parser consumer that Phase 5 may never use).

### Project and targeting

One new product project, `src/AutoGIS.Civil3D.Adapter/`:

- `net8.0-windows`, x64, `TreatWarningsAsErrors` and the other
  repository-wide settings inherited from `Directory.Build.props`, which
  gains a general opt-out for projects that declare their own target
  framework instead of the diagnostics-only name test it carries today.
- References the validator library and the three AutoCAD compile assemblies
  (`AcCoreMgd`, `AcDbMgd`, `AcMgd`) supplied by Autodesk's pinned packages.
  Runtime assets are excluded; nothing Autodesk is copied to output or
  redistributed. The Civil assembly identity is resolved only inside the
  guarded runtime host check.
- A member of `AutoGIS.Civil3D.sln`, so the existing CI restore, build,
  test, and format steps cover it without workflow changes. CI already
  runs on Windows runners.
- Contains no commands, no drawing access, and no extension-application
  entry point. Its only code is the smallest compile-time touch of both an
  AutoCAD and a Civil 3D API type, reporting the bound API versions, so
  that reference resolution is exercised rather than assumed. Phase 5 owns
  everything that runs inside Civil 3D.

Single development and qualification target:
[ADR-0008](../../adr/0008-civil3d-2026-development-target.md).
The release boundary is a rule, not code: the release appears only in the
central package pins, build property, and assembly's release stamp, never in
namespaces, type names, or project names. A second supported release requires
a separate decision and belongs to Phase 7 with the rest of packaging and
compatibility.

A test project, `tests/AutoGIS.Civil3D.Adapter.Tests/`, is created only
when the first logic that runs without Civil 3D lands there; Phase 4
expects none. Seam additions to the validator are covered by the existing
validator suite.

### Reference-assembly sourcing

Compile the AutoCAD surface from Autodesk's `AutoCAD.NET`,
`AutoCAD.NET.Core`, and `AutoCAD.NET.Model` packages pinned to 25.1.0, as
specified by [ADR-0009](../../adr/0009-hosted-ci-manual-integration.md). Locked
restore, excluded runtime assets, resolved-name and 25.1-series checks, and
product-only artifact validation prevent an Autodesk binary from entering the
preview. The guarded Civil identity lookup validates the installed 13.8 API at
runtime; it is not a general reference source for future Civil operations.

[ADR-0007](../../adr/0007-civil3d-2025-reference-sourcing.md) and ADR-0008
preserve the historical source decisions they record; neither is a fallback.

## Implementation boundary

This design reserves these path prefixes in the roadmap marker, per the
phase-gate lifecycle:

- `src/AutoGIS.Civil3D.Adapter/` — the adapter project itself.
- `tests/AutoGIS.Civil3D.Adapter.Tests/` — its future test project, so
  adapter tests cannot land ahead of the adapter plan.

Not reserved, deliberately: the validator library and its tests, the
solution file, and the build property files. They are accepted-phase
deliverables under ordinary maintenance, and the seam additions and
opt-out change land with the Phase 4 implementation plan, reviewed as
product code under [ADR-0004](../../adr/0004-one-adversarial-review-proportioned-to-risk.md).

## Acceptance evidence

Collected on a Phase 4 gate issue and cited by the eventual gate-change-log
row, following the Phase 0 and Phase 3 pattern:

- The adapter project restores in locked mode, builds and runs its unit tests
  on ordinary hosted Windows CI, and produces the product-only 2026 preview
  artifact with the exact commit and product DLL hashes.
- The target/reference checks demonstrably fail: an unsupported year or
  missing, misnamed, or wrong-series AutoCAD reference refuses the build.
- The seam members public and exercised by the validator suite, with the
  validator still building and testing with no Autodesk dependency.
- The ADR-0008 sourcing amendment accepted and the
  [architecture map](../../architecture.md) naming the adapter as the one
  product project that may reference Autodesk.

Build-only merge eligibility does not replace native evidence. The manual
preview handoff under ADR-0009 checks `NETLOAD`, command binding and the modal
preview on the licensed 2026 host. Complete workflow qualification remains a
Phase 4 acceptance requirement.

## Exclusions

- No Civil 3D commands, drawing reads, imports, or transactions (Phase 5
  and later).
- No bundle, `PackageContents.xml`, installer, or signing work (Phase 7).
- No multi-targeting (Phase 7); the single target follows ADR-0008.
- No contract change: v1 is frozen.
- No change to the diagnostic kit or its preserved evidence.
- No parking-lot items.

## Known ceilings

- Hosted compilation covers the current AutoCAD API surface only. Future Civil
  API operations still need a supported reference source and native evidence.
- The series check proves major.minor agreement, not binary compatibility
  with a specific Civil 3D update; the pilot run showed the reference and
  runtime builds differ in the fourth version part and bind correctly.
- Nothing in Phase 4 runs inside Civil 3D, so the foundation is proven by
  compilation and binding evidence, not by execution.
