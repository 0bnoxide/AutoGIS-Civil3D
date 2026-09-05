# ADR-0007: Compile the adapter against pinned Civil 3D 2025 reference packages

**State:** Accepted
**Date:** 2026-09-04

**Target amendment:** [ADR-0008](0008-civil3d-2026-development-target.md)
supersedes this ADR's 2025 target, exact pins, and release-specific series
requirements for active development. The table and rationale below preserve
the historical 2025 decision, not an authorized fallback for the new target.
The pinned-package sourcing policy, non-copying references, fail-closed
checks, and separate owner decision for a source change remain governing.

## Context

The [retained adapter sourcing decision](../superpowers/specs/2026-09-04-phase-4-adapter-foundation-design.md#reference-assembly-sourcing),
as qualified by [ADR-0006](0006-civil-production-accelerator.md), requires
the product adapter to build in ordinary Windows CI without an installed
Civil 3D host. The [2025 diagnostic evidence](../diagnostics/2026-08-04-live-run-civil3d-2025.md#build-provenance)
records the reference packages and their binding to the real 2025 runtime.
This ADR records that retained choice for the New Proposal adapter; it does
not extend the supported release or substitute diagnostic evidence for the
workflow's native qualification.

## Decision

Use the following exact NuGet pins through `Directory.Packages.props` and
checked-in package lock files:

| Package | Version | Selected assembly | Package-relative path | Assembly version |
|---|---|---|---|---|
| [AutoCAD.NET](https://www.nuget.org/packages/AutoCAD.NET/25.0.1) | 25.0.1 | AcMgd | `lib/net8.0/AcMgd.dll` | 25.0.0.0 |
| [AutoCAD.NET.Core](https://www.nuget.org/packages/AutoCAD.NET.Core/25.0.0) | 25.0.0 | AcCoreMgd | `lib/net8.0/AcCoreMgd.dll` | 25.0.0.0 |
| [AutoCAD.NET.Model](https://www.nuget.org/packages/AutoCAD.NET.Model/25.0.0) | 25.0.0 | AcDbMgd | `lib/net8.0/AcDbMgd.dll` | 25.0.0.0 |
| [Chuongmep.Civil3D.Api.AecBaseMgd](https://www.nuget.org/packages/Chuongmep.Civil3D.Api.AecBaseMgd/2025.0.0) | 2025.0.0 | AecBaseMgd | `lib/net8/AecBaseMgd.dll` | 8.7.49.0 |
| [Chuongmep.Civil3D.Api.AeccDbMgd](https://www.nuget.org/packages/Chuongmep.Civil3D.Api.AeccDbMgd/2025.0.0) | 2025.0.0 | AeccDbMgd | `lib/net8/AeccDbMgd.dll` | 13.7.0.154 |

The AutoCAD packages are Autodesk's packages; the Civil 3D packages are
community reference sources retained from the diagnostic build. Package
assets are excluded from automatic compile/runtime imports and remain
private to the adapter. Explicit `Reference` items select these assemblies
with `Private=false`; no Autodesk assembly is copied to output or committed.

Target `net8.0-windows`, x64, with built-in Windows Forms support. The WPF
framework reference supplies WindowsBase 8 used by Autodesk API signatures;
the wizard uses Windows Forms. Preserve the repository's common build
settings and the independent diagnostic project's exclusion.

Before assembly resolution, built-in MSBuild `GetAssemblyIdentity` and
`Error` tasks require readable references and reject AutoCAD assemblies
outside 25.0 and AeccDbMgd outside 13.7. AecBaseMgd keeps its independent
version series, matching the retained diagnostic rule. Explicit path
properties permit a controlled negative build probe against a mismatched
reference; they do not authorize a different release target.

## Alternatives

- Installed-product discovery makes CI depend on a licensed workstation;
  it remains the diagnostic kit's path, not this product build's source.
- Vendoring or redistributing Autodesk DLLs is not an accepted source.
- A new script or custom build task adds no value over MSBuild's existing
  identity and error tasks.

## Consequences

Locked restore and a failing-capable series check establish build provenance,
not compatibility with every patch release. If a pinned package becomes
unavailable, delivery is blocked for an owner sourcing decision rather than
silently selecting another package or host. A different release requires
its own decision and qualification.

The renderer, compatibility predicate and immutable preview snapshot are
separate from the native command in the same adapter assembly, allowing
ordinary tests without loading Autodesk types. New Proposal acceptance
still requires the [design's live-host evidence](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md#acceptance-evidence).
