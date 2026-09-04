# AutoGIS-Civil3D

A Civil Production Accelerator: deterministic, previewable, auditable automations that cut project setup, model checking, repetitive design production, and deliverable preparation time in Civil 3D. The first production workflow is `New Proposal`, a native command that scaffolds a proposal project ([ADR-0006](docs/adr/0006-civil-production-accelerator.md)).

## Current slice

- One versioned ZIP containing `handoff.json` and one LandXML 1.2 TIN surface.
- A pure .NET 8 validator and CLI with no Autodesk or ArcGIS runtime dependency.
- Synthetic golden packages for conformance and regression testing.
- A preserved read-only Civil 3D diagnostic kit for later authorized workstation validation.

Contract validation proves package conformance only. It does not prove that Civil 3D imported the surface. The handoff contract and its validator are supported infrastructure beneath the production workflows the [roadmap](docs/roadmap.md) sequences.

## Quick start

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
dotnet test AutoGIS.Civil3D.sln -c Release --no-build
dotnet run --project src/AutoGIS.Civil3D.Handoff.Cli -- fixtures/v1/valid/known-vertical-datum.zip
```

The CLI exits `0` for a valid package, `1` for an invalid package, `2` for a
valid package that requires vertical-datum review, and `3` for usage or
operational failures. Exit `2` is not approval to import: resolve the vertical
datum before Civil 3D use.

See the [v1 contract](contract/v1/README.md), [roadmap](docs/roadmap.md), [architecture handoff](docs/architecture-handoff.md), [ADR-0001](docs/adr/0001-handoff-contract-ownership.md), [fixtures](fixtures/v1/README.md), the [live Civil 3D 2025 diagnostic run](docs/diagnostics/2026-08-04-live-run-civil3d-2025.md), and the approved design under `docs/superpowers/specs/`.
