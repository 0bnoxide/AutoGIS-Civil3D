#!/usr/bin/env python3
"""Static validation that does not require .NET or Autodesk assemblies."""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "AutoGIS.Civil3D.Diagnostics" / "AutoGIS.Civil3D.Diagnostics.csproj"
SOURCE_ROOT = ROOT / "src" / "AutoGIS.Civil3D.Diagnostics"
COMMAND_SOURCE = SOURCE_ROOT / "DiagnosticsCommands.cs"
MANIFEST = ROOT / "bundle" / "AutoGIS.Civil3D.Diagnostics.bundle" / "PackageContents.xml"
BUILD_SCRIPT = ROOT / "scripts" / "Build-Diagnostics.ps1"
INSTALL_SCRIPT = ROOT / "scripts" / "Install-CurrentUser.ps1"
UNINSTALL_SCRIPT = ROOT / "scripts" / "Uninstall-CurrentUser.ps1"
UTILITIES_SCRIPT = ROOT / "scripts" / "Utilities.ps1"
WINDOWS_SCRIPT_TEST = ROOT / "tests" / "validate_windows_scripts.py"
EXPECTED_VERSION = "0.1.2"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    required_files = (
        PROJECT,
        COMMAND_SOURCE,
        MANIFEST,
        BUILD_SCRIPT,
        INSTALL_SCRIPT,
        UNINSTALL_SCRIPT,
        UTILITIES_SCRIPT,
        WINDOWS_SCRIPT_TEST,
    )
    for path in required_files:
        require(path.is_file(), f"Missing required file: {path.relative_to(ROOT)}")

    source_files = sorted(SOURCE_ROOT.glob("*.cs"))
    require(bool(source_files), "No C# source files were found")
    project_text = PROJECT.read_text(encoding="utf-8")
    command_text = COMMAND_SOURCE.read_text(encoding="utf-8")
    build_text = BUILD_SCRIPT.read_text(encoding="utf-8")
    install_text = INSTALL_SCRIPT.read_text(encoding="utf-8")
    uninstall_text = UNINSTALL_SCRIPT.read_text(encoding="utf-8")
    utilities_text = UTILITIES_SCRIPT.read_text(encoding="utf-8")

    require("<TargetFramework>net8.0-windows</TargetFramework>" in project_text, "Project must target net8.0-windows")
    require("<PlatformTarget>x64</PlatformTarget>" in project_text, "Project must target x64")
    require(f"<Version>{EXPECTED_VERSION}</Version>" in project_text, "Project version is inconsistent")
    require(f"<AssemblyVersion>{EXPECTED_VERSION}.0</AssemblyVersion>" in project_text, "Assembly version is inconsistent")
    require(f"<FileVersion>{EXPECTED_VERSION}.0</FileVersion>" in project_text, "File version is inconsistent")
    require('FrameworkReference Include="Microsoft.WindowsDesktop.App"' in project_text, "Windows Desktop framework reference is missing")
    for assembly in ("AcCoreMgd", "AcDbMgd", "AcMgd", "AecBaseMgd", "AeccDbMgd"):
        require(f'<Reference Include="{assembly}">' in project_text, f"Missing reference: {assembly}")
    require(project_text.count("<Private>false</Private>") == 5, "Autodesk references must not be copied locally")

    require('[CommandMethod("AUTOGISDIAGNOSTICS", CommandFlags.Modal)]' in command_text, "Diagnostic command is not registered")
    require("CivilApplication.ActiveDocument" in command_text, "Civil 3D API is not exercised")
    require("SECURELOAD" in command_text and "TRUSTEDPATHS" in command_text, "Security settings are not reported")
    require("allSectionsSucceeded" in command_text and "PARTIAL" in command_text, "Final result must reflect section failures")
    require(f"AutoGIS Civil 3D Diagnostics {EXPECTED_VERSION}" in command_text, "Command banner version is inconsistent")
    require('WriteLine(editor, "PASS"' not in command_text, "A preliminary load result must not be labeled as the final PASS")
    require('WriteLine(editor, "LOAD"' in command_text, "The preliminary plug-in load result must be labeled LOAD")
    require('WriteLine(editor, "PRIVACY"' in command_text, "The command must warn that diagnostic paths need redaction")

    forbidden = (
        "System.IO",
        "System.Net",
        "Microsoft.Win32",
        "System.Diagnostics.Process",
        "Process.Start",
        "ProcessStartInfo",
        "File.",
        "Directory.",
        "Registry.",
        "File.Write",
        "FileStream",
        "HttpClient",
        "WebRequest",
        "Socket",
        "TcpClient",
        "UdpClient",
        "SetSystemVariable",
        "StartTransaction",
        "OpenMode.ForWrite",
        "SaveAs",
        "Erase(",
        "SendStringToExecute",
    )
    for source_file in source_files:
        source_text = source_file.read_text(encoding="utf-8")
        for token in forbidden:
            require(
                token not in source_text,
                f"Read-only source {source_file.name} contains forbidden token: {token}",
            )

    xml_root = ET.parse(MANIFEST).getroot()
    require(xml_root.tag == "ApplicationPackage", "Unexpected manifest root")
    require(xml_root.attrib.get("AppVersion") == EXPECTED_VERSION, "Manifest AppVersion is inconsistent")
    component = xml_root.find("./Components/ComponentEntry")
    require(component is not None, "ComponentEntry is missing")
    require(component.attrib.get("Version") == EXPECTED_VERSION, "Manifest component version is inconsistent")
    require(component.attrib.get("AppType") == ".Net", "Component must be identified as a .NET plug-in")
    runtime = component.find("./RuntimeRequirements")
    require(runtime is not None, "RuntimeRequirements is missing")
    require(runtime.attrib.get("Platform") == "Civil3D", "Bundle must target Civil3D")
    require(runtime.attrib.get("OS") == "Win64", "Bundle must target Win64")
    require(runtime.attrib.get("SeriesMin") == "R25.0", "SeriesMin must be R25.0")
    require(runtime.attrib.get("SeriesMax") == "R25.0", "SeriesMax must be R25.0")
    require(
        component.attrib.get("ModuleName") == "./Contents/2025/AutoGIS.Civil3D.Diagnostics.dll",
        "Unexpected module path",
    )
    command = component.find("./Commands/Command")
    require(command is not None and command.attrib.get("Global") == "AUTOGISDIAGNOSTICS", "Manifest command mismatch")

    require("dotnet" in build_text, "Build script does not invoke dotnet")
    require("Civil3D_2025_ROOT" in build_text or "CIVIL3D_2025_ROOT" in build_text, "Build script lacks Civil 3D root override")
    require("@(13, 7)" in build_text and "@(25, 0)" in build_text, "Build script must reject cross-version Autodesk assemblies")
    require("Utilities.ps1" in build_text, "Build script must load the shared utilities")
    require("Get-AutoGISFileSha256" in build_text, "Build script must verify the copied DLL hash")
    require("Get-FileHash" not in build_text, "Build script must not depend on PowerShell module autoloading for hashes")

    require("$env:APPDATA" in install_text, "Installer must use the current-user APPDATA location")
    require("ProgramFiles" not in install_text, "Current-user installer must not write to Program Files")
    require("PackageContents.xml" in install_text, "Installer must preflight the bundle manifest")
    require("stagingBundle" in install_text, "Installer must stage before changing the active bundle")
    require("Get-AutoGISFileSha256" in install_text, "Installer must verify source, staged, and installed hashes")
    require("New-AutoGISUniqueChildPath" in install_text, "Installer backups must be collision resistant")
    require("previousBundleBackup" in install_text and "Rollback was incomplete" in install_text, "Installer must restore the previous bundle after activation failure")
    require("Assert-AutoGISDirectoryTreeHasNoReparsePoints" in install_text, "Installer must reject linked bundle trees")
    require("Get-FileHash" not in install_text, "Installer must not depend on PowerShell module autoloading for hashes")

    require("New-AutoGISUniqueChildPath" in uninstall_text, "Uninstall backups must be collision resistant")
    require("Assert-AutoGISDirectoryTreeHasNoReparsePoints" in uninstall_text, "Uninstaller must reject linked bundle trees")
    require("SHA256" in utilities_text, "Shared utilities must provide module-independent SHA-256 hashing")

    print("Static validation passed: versions, all C# sources, manifest, build, staged install, and uninstall.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as exc:
        print(f"VALIDATION FAILED: {exc}", file=sys.stderr)
        raise SystemExit(1)
