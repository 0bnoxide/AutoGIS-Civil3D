#!/usr/bin/env python3
"""Functional validation of the Windows CMD and PowerShell deployment path."""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BUILD_SCRIPT = ROOT / "scripts" / "Build-Diagnostics.ps1"
INSTALL_SCRIPT = ROOT / "scripts" / "Install-CurrentUser.ps1"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def run_wrapper(
    comspec: str,
    fixture: Path,
    wrapper: str,
    environment: dict[str, str],
    *,
    expect_success: bool,
) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        [comspec, "/d", "/c", str(fixture / wrapper)],
        cwd=fixture,
        env=environment,
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    combined = "\n".join(part for part in (result.stdout, result.stderr) if part)
    if expect_success:
        require(result.returncode == 0, f"{wrapper} failed with {result.returncode}:\n{combined}")
    else:
        require(result.returncode != 0, f"{wrapper} unexpectedly succeeded:\n{combined}")
    return result


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    if os.name != "nt":
        print("Windows wrapper validation skipped: non-Windows host.")
        return 0

    comspec = os.environ.get("COMSPEC") or shutil.which("cmd.exe")
    require(bool(comspec), "cmd.exe was not found")

    for script in (BUILD_SCRIPT, INSTALL_SCRIPT):
        require(
            "Get-FileHash" not in script.read_text(encoding="utf-8"),
            f"{script.name} relies on module-dependent Get-FileHash",
        )

    with tempfile.TemporaryDirectory(prefix="autogis-civil3d-wrapper-") as temp_name:
        temp_root = Path(temp_name)
        fixture = temp_root / "fixture"
        shutil.copytree(ROOT, fixture, ignore=shutil.ignore_patterns("bin", "obj", "*.dll", "*.pdb"))

        source_dll = (
            fixture
            / "bundle"
            / "AutoGIS.Civil3D.Diagnostics.bundle"
            / "Contents"
            / "2025"
            / "AutoGIS.Civil3D.Diagnostics.dll"
        )
        source_dll.write_bytes(b"AutoGIS Civil 3D wrapper-test fixture\n")
        expected_hash = sha256(source_dll)

        appdata = temp_root / "profile" / "Roaming"
        localappdata = temp_root / "profile" / "Local"
        appdata.mkdir(parents=True)
        localappdata.mkdir(parents=True)
        environment = os.environ.copy()
        environment["APPDATA"] = str(appdata)
        environment["LOCALAPPDATA"] = str(localappdata)

        target_bundle = appdata / "Autodesk" / "ApplicationPlugins" / "AutoGIS.Civil3D.Diagnostics.bundle"
        target_dll = target_bundle / "Contents" / "2025" / "AutoGIS.Civil3D.Diagnostics.dll"
        backup_root = localappdata / "AutoGIS" / "PluginBackups"

        run_wrapper(comspec, fixture, "install-current-user.cmd", environment, expect_success=True)
        require(target_dll.is_file(), "Initial install did not produce the expected DLL")
        require(sha256(target_dll) == expected_hash, "Initial installed DLL hash mismatch")

        run_wrapper(comspec, fixture, "install-current-user.cmd", environment, expect_success=True)
        run_wrapper(comspec, fixture, "install-current-user.cmd", environment, expect_success=True)
        backups = sorted(path for path in backup_root.iterdir() if path.is_dir())
        require(len(backups) == 2, f"Expected two reinstall backups, found {len(backups)}")
        require(len({path.name for path in backups}) == 2, "Reinstall backup names collided")
        for backup in backups:
            require(
                (backup / "Contents" / "2025" / "AutoGIS.Civil3D.Diagnostics.dll").is_file(),
                f"Backup is incomplete: {backup}",
            )

        manifest = fixture / "bundle" / "AutoGIS.Civil3D.Diagnostics.bundle" / "PackageContents.xml"
        saved_manifest = manifest.read_bytes()
        manifest.unlink()
        run_wrapper(comspec, fixture, "install-current-user.cmd", environment, expect_success=False)
        require(target_dll.is_file(), "Failed source validation removed the installed bundle")
        require(sha256(target_dll) == expected_hash, "Failed source validation changed the installed DLL")
        require(
            len([path for path in backup_root.iterdir() if path.is_dir()]) == 2,
            "Failed source validation created a backup or moved the installed bundle",
        )
        manifest.write_bytes(saved_manifest)

        run_wrapper(comspec, fixture, "uninstall-current-user.cmd", environment, expect_success=True)
        require(not target_bundle.exists(), "Uninstall left the target bundle in the loader path")
        backups = sorted(path for path in backup_root.iterdir() if path.is_dir())
        require(len(backups) == 3, f"Expected three recoverable backups, found {len(backups)}")
        require(len({path.name for path in backups}) == 3, "Uninstall backup name collided")

        run_wrapper(comspec, fixture, "uninstall-current-user.cmd", environment, expect_success=True)
        require(
            len([path for path in backup_root.iterdir() if path.is_dir()]) == 3,
            "No-op uninstall unexpectedly changed the backups",
        )

    print("Windows wrapper validation passed: install, backup, fail-closed preflight, and uninstall.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as exc:
        print(f"VALIDATION FAILED: {exc}", file=os.sys.stderr)
        raise SystemExit(1)
