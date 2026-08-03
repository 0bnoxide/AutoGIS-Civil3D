# AutoGIS-Civil3D

Contract-first handoff tooling between AutoGIS surface exports and a future Civil 3D adapter.

## Current slice

- One versioned ZIP containing `handoff.json` and one LandXML 1.2 TIN surface.
- A pure .NET 8 validator and CLI with no Autodesk or ArcGIS runtime dependency.
- Synthetic golden packages for conformance and regression testing.
- A preserved read-only Civil 3D diagnostic kit for later authorized workstation validation.

Contract validation proves package conformance only. It does not prove that Civil 3D imported the surface.

See `docs/architecture-handoff.md`, ADR-0001, and the approved design under `docs/superpowers/specs/`.
