# AutoGIS-Civil3D

Contract-first handoff tooling between AutoGIS surface exports and a future Civil 3D adapter.

## Current slice

- One versioned ZIP containing `handoff.json` and one LandXML 1.2 TIN surface.
- A pure .NET 8 validator and CLI with no Autodesk or ArcGIS runtime dependency.
- Synthetic golden packages for conformance and regression testing.
- A preserved read-only Civil 3D diagnostic kit for later authorized workstation validation.

Contract validation proves package conformance only. It does not prove that Civil 3D imported the surface.

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

See the [v1 contract](contract/v1/README.md), [architecture handoff](docs/architecture-handoff.md), [ADR-0001](docs/adr/0001-handoff-contract-ownership.md), [fixtures](fixtures/v1/README.md), and the approved design under `docs/superpowers/specs/`.
