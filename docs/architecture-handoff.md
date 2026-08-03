# AutoGIS to Civil 3D handoff architecture

AutoGIS will later produce a versioned ZIP containing exactly `handoff.json` and `surface.landxml`. The pure .NET 8 validator in this repository checks that package before any Autodesk API is involved.

Dependency direction is AutoGIS producer -> language-neutral contract -> pure validator <- future Civil 3D adapter. The pure validator never references ArcGIS, AutoCAD, or Civil 3D assemblies.

Version 1 carries one LandXML 1.2 TIN surface. DWG/DXF, multiple surfaces, producer integration, Autodesk adapter code, and live import automation are separate future slices.

Contract-valid means structurally and semantically conformant. It does not mean Civil 3D import-tested.
