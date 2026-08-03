# AutoGIS to Civil 3D handoff architecture

AutoGIS will later produce a versioned ZIP containing exactly `handoff.json` and `surface.landxml`. The pure .NET 8 validator in this repository checks that package before any Autodesk API is involved.

Dependency direction is AutoGIS producer -> language-neutral contract -> pure validator <- future Civil 3D adapter. The pure validator never references ArcGIS, AutoCAD, or Civil 3D assemblies.

Version 1 carries one LandXML 1.2 TIN surface. DWG/DXF, multiple surfaces, producer integration, Autodesk adapter code, and live import automation are separate future slices.

Contract-valid means structurally and semantically conformant. It does not mean Civil 3D import-tested.

## Handoff assets

- [V1 contract guide](../contract/v1/README.md) and its normative [JSON Schema](../contract/v1/handoff-manifest.schema.json)
- Pure [.NET validator](../src/AutoGIS.Civil3D.Handoff/) and [CLI](../src/AutoGIS.Civil3D.Handoff.Cli/)
- Synthetic [conformance fixtures](../fixtures/v1/README.md)
- Preserved [diagnostic artifacts](../artifacts/diagnostics/README.md) and their [audit](diagnostics/diagnostic-kit-audit.md)

## Deferred live gate

The diagnostic kit is retained for a separate, authorized workstation gate. Build and load diagnostic kit 0.1.1 on a Civil 3D 2025 workstation, run `AUTOGISDIAGNOSTICS` with a blank or sanitized drawing, and retain sanitized evidence. This contract slice does not claim that this gate, `NETLOAD`, or Civil 3D import has run successfully.
