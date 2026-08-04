# AutoGIS Civil 3D 2025 Diagnostic Plug-in

This package builds a deliberately read-only Civil 3D 2025 command:

```text
AUTOGISDIAGNOSTICS
```

> **Build status:** This download is a source/build kit, not a precompiled DLL.
> Run `build.cmd` on the Civil 3D 2025 workstation; it compiles against that
> machine's Autodesk assemblies and places the finished DLL in the bundle.

The scripts in this source kit are not code-signed. A downloaded ZIP and its
extracted files may also carry Windows' internet-origin marker. If Windows,
PowerShell, AppLocker, WDAC, or endpoint protection blocks a script, record the
exact message and send it to CAD/IT. Do not use `Unblock-File`, weaken the
execution policy, or bypass a company security control for this pilot.

The command verifies that a managed .NET plug-in can load on the workstation and
prints useful host, security, drawing, Civil 3D settings, and object-count
information to the Civil 3D command line.

It does **not**:

- modify or save the drawing;
- write files;
- change registry or Civil 3D settings;
- start Python, AutoGIS, PowerShell, or any child process;
- access the network.

## Why the DLL is built on the Civil 3D workstation

Civil 3D plug-ins must reference Autodesk's installed managed assemblies. Those
assemblies are not redistributed in this package. `Build-Diagnostics.ps1`
discovers the Civil 3D 2025 files already installed on the workstation and
passes their paths to the .NET build.

## Prerequisites

- Autodesk Civil 3D 2025 installed.
- Microsoft .NET 8 SDK installed (`dotnet --list-sdks` must show an `8.x` SDK).
  The runtime included with Civil 3D is not sufficient to compile source.
- Permission under company policy to load a custom managed plug-in.

Building and installing to the current-user plug-in folder normally do not need
Windows administrator privileges. Company application-control policies may
still require CAD/IT approval.

## Build

1. Extract the package to a local folder.
2. Close Civil 3D if an earlier diagnostic build is loaded.
3. Double-click `build.cmd`, or run:

   ```powershell
   .\scripts\Build-Diagnostics.ps1
   ```

The script searches the standard Civil 3D 2025 installation beneath:

```text
C:\Program Files\Autodesk\AutoCAD 2025
```

For a nonstandard installation, pass its root explicitly:

```powershell
.\scripts\Build-Diagnostics.ps1 -Civil3DRoot "D:\Autodesk\AutoCAD 2025"
```

Successful compilation produces:

```text
src\AutoGIS.Civil3D.Diagnostics\bin\Release\net8.0-windows\AutoGIS.Civil3D.Diagnostics.dll
```

and copies it into the ready-to-install bundle.

## Test with NETLOAD first

To make the smallest possible security test:

1. Start Civil 3D 2025 with a blank or sanitized drawing.
2. Enter `NETLOAD`.
3. Select the compiled DLL under `src\...\bin\Release\net8.0-windows`.
4. Enter `AUTOGISDIAGNOSTICS`.

If Civil 3D blocks the assembly, record the message and the current values of
`SECURELOAD`, `TRUSTEDPATHS`, and `APPAUTOLOAD`. Do not weaken company-managed
security settings.

## Per-user installation

After the NETLOAD test succeeds, double-click `install-current-user.cmd`, or
run:

```powershell
.\scripts\Install-CurrentUser.ps1
```

The script first copies the bundle to a uniquely named staging directory,
verifies the staged manifest and DLL hashes, and only then activates it at:

```text
%APPDATA%\Autodesk\ApplicationPlugins\AutoGIS.Civil3D.Diagnostics.bundle
```

An existing diagnostic bundle is moved to a uniquely named backup under
`%LOCALAPPDATA%\AutoGIS\PluginBackups`; it is not deleted. If activation or
post-install verification fails, the installer preserves the failed new bundle
when possible and restores the prior bundle.

Restart Civil 3D or run `APPAUTOLOADER`, choose **Reload**, and then enter:

```text
AUTOGISDIAGNOSTICS
```

## Uninstall

Close Civil 3D, then run `uninstall-current-user.cmd`. The installed bundle is
moved to the same recoverable backup directory rather than permanently deleted.

## Expected output

The command reports:

- plug-in, .NET runtime, AutoCAD API, and Civil 3D API versions;
- process architecture and plug-in path;
- `ACADVER`, `SECURELOAD`, `TRUSTEDPATHS`, and `APPAUTOLOAD`;
- active drawing name and AutoCAD insertion units;
- Civil 3D drawing units, foot conversion, coordinate-system code, angular
  units, and drawing scale;
- counts of COGO points, surfaces, alignments, sites, corridors, and gravity
  pipe networks.

Every section is guarded independently so a missing setting or unusual drawing
does not prevent the remaining diagnostics from running.

Diagnostic output can include the active drawing name/path, plug-in path, and
trusted-path locations. Use a blank or sanitized drawing and redact paths before
sharing output outside the approved internal pilot team.

## Validate the source kit

These checks do not require Civil 3D or the .NET SDK:

```powershell
python .\tests\validate_package.py
python .\tests\validate_windows_scripts.py
```

The second check runs the actual Windows `.cmd` installer and uninstaller in an
isolated temporary profile. A successful local build and a live Civil 3D
`NETLOAD`/`AUTOGISDIAGNOSTICS` run are still required on the pilot workstation.

## 2026 support

This pilot bundle intentionally targets Civil 3D 2025 only (`R25.0`). A 2026
host should be compiled against the Civil 3D 2026 assemblies and targeted to
`R25.1`; do not relabel this 2025 test bundle as a 2026 build.

## Autodesk references

- [Civil 3D 2025: create a .NET 8, x64 class library](https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-A31588E9-2A5F-4BF1-878D-DBE2564E2A99.htm)
- [Civil 3D 2025: add the five base managed references](https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-267E68C8-AD2D-4F7F-87DF-831018D56CDB.htm)
- [Civil 3D 2025: target `R25.0`, `Civil3D`, and `Win64` in PackageContents.xml](https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-6FDC9D3D-FAB2-453E-A7BF-F1CC82F4AE18.htm)
- [AutoCAD 2025: scope RuntimeRequirements to the .NET component](https://blog.autodesk.io/autocad-2025-update-your-packagecontentsxml-with-runtimerequirements/)
- [AutoCAD 2025 managed compatibility: release 25.0 uses .NET 8](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm)
