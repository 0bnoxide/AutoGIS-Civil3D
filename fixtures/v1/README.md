# Version 1 handoff fixtures

These packages are deterministic conformance fixtures for the version 1 LandXML handoff validator. All identifiers, metadata, coordinates, and TIN geometry are synthetic; no package contains customer, project, survey, or production data.

Regenerate the complete fixture set from the repository root with exactly:

```powershell
dotnet run --project tools/AutoGIS.Civil3D.FixtureBuilder/AutoGIS.Civil3D.FixtureBuilder.csproj -- fixtures/v1
```

The generator writes ZIP entries in a fixed order (`handoff.json`, then `surface.landxml`), assigns every entry the fixed timestamp `2026-08-02T00:00:00`, and fixes compression, host-system, and regular-file attributes. The invalid encrypted, unsupported-compression, declared-size, and symbolic-link fixtures are produced by deterministic local- and central-directory header mutations after ordinary ZIP creation. Those mutations exercise archive validation without embedding executable content or external references.

Do not edit fixture ZIP bytes manually. Change the catalog recipe, regenerate the full set with the command above, and run the golden conformance tests so the checked-in packages and generator remain byte-for-byte identical.

`valid/unknown-vertical-datum.zip` is intentionally valid with warning `WRN001`: its package structure, integrity, LandXML, and cross-checks are valid, while the manifest explicitly records that the vertical datum is unknown and must be confirmed before import. It is not an invalid package and does not infer a datum.
