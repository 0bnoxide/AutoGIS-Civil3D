# AutoGIS Civil 3D Diagnostics — IT/CAD Pilot Notes

## Purpose

Validate whether an internally developed, read-only .NET 8 plug-in can load in
Autodesk Civil 3D 2025 before development of the AutoGIS/Civil 3D bridge.

## Command

```text
AUTOGISDIAGNOSTICS
```

## Runtime behavior

- Loads only in 64-bit Civil 3D release series `R25.0` (2025).
- Reads host, drawing, Civil 3D setting, and object-count information.
- Writes output only to the Civil 3D command line.
- Makes no drawing or database changes.
- Does not save the drawing.
- Performs no file, registry, network, HTTP, socket, or subprocess operations.
- Does not start AutoGIS or Python.
- Requires no elevated privileges at runtime.

## Autodesk references

The project compiles against the managed assemblies already installed with
Civil 3D 2025. Autodesk assemblies are marked `Private=false` and are not copied
or redistributed with the plug-in.

## Pilot deployment

The included current-user installer copies the bundle to:

```text
%APPDATA%\Autodesk\ApplicationPlugins\AutoGIS.Civil3D.Diagnostics.bundle
```

For organization-managed deployment, IT may instead place an approved/signed
bundle under:

```text
%PROGRAMFILES%\Autodesk\ApplicationPlugins
```

The source package does not change `SECURELOAD`, `TRUSTEDPATHS`, `APPAUTOLOAD`,
Windows Defender, AppLocker, WDAC, or endpoint-protection policies.

The source-kit scripts are not code-signed, and downloaded files may carry a
Windows internet-origin marker. If a script is blocked, capture the exact error
for IT review. Do not use `Unblock-File`, change execution policy, or weaken an
application-control setting to make the pilot run.

The current-user installer stages the bundle and verifies its manifest and DLL
hashes before replacing an existing installation. Backup names include a
high-resolution timestamp and random suffix. If activation fails, the installer
attempts to preserve the failed bundle and restore the previous one.

## Evidence handling

Command output can contain the active drawing name/path, plug-in location, and
`TRUSTEDPATHS`. Run against a blank or sanitized drawing and redact paths before
sharing evidence outside the approved internal pilot team.

## Removal

The uninstall script moves only the specifically named diagnostic bundle into
a uniquely named recoverable backup under `%LOCALAPPDATA%\AutoGIS\PluginBackups`.
