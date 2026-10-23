import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "package-preview.ps1"
INVENTORY = ROOT / "tools" / "preview-inventory.ps1"
POWERSHELL = shutil.which("powershell") or shutil.which("pwsh") or "powershell"
PRODUCT_DLLS = (
    "AutoGIS.Civil3D.Adapter.dll",
    "AutoGIS.Civil3D.Proposal.dll",
)
COMMIT = "0123456789abcdef0123456789abcdef01234567"


class PackagePreviewTests(unittest.TestCase):
    def run_package(self, source: Path, destination: Path, year: str = "2026") -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                POWERSHELL,
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(SCRIPT),
                "-Year",
                year,
                "-Commit",
                COMMIT,
                "-SourceBuildDirectory",
                str(source),
                "-DestinationDirectory",
                str(destination),
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )

    def test_packages_only_product_files_and_records_both_hashes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary = Path(temporary_directory)
            source = temporary / "source"
            destination = temporary / "destination"
            source.mkdir()
            expected_hashes = {}
            for index, name in enumerate(PRODUCT_DLLS, start=1):
                content = f"synthetic product {index}\n".encode()
                (source / name).write_bytes(content)
                expected_hashes[name] = hashlib.sha256(content).hexdigest()

            result = self.run_package(source, destination)

            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual(
                sorted((*PRODUCT_DLLS, "build-info.json", "new-proposal-preview.md", "new-proposal-smoke.scr")),
                sorted(path.name for path in destination.iterdir()),
            )
            build_info = json.loads((destination / "build-info.json").read_text(encoding="utf-8-sig"))
            self.assertEqual("2026", build_info["civil3dYear"])
            self.assertEqual(COMMIT, build_info["commit"])
            self.assertEqual(
                expected_hashes,
                {entry["path"]: entry["sha256"] for entry in build_info["productDlls"]},
            )
            self.assertIn("manual", build_info["qualification"].lower())

    def test_rejects_unexpected_autodesk_dll(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            source = Path(temporary_directory) / "source"
            source.mkdir()
            for name in PRODUCT_DLLS:
                (source / name).write_bytes(b"product")
            (source / "AcMgd.dll").write_bytes(b"must not ship")

            result = self.run_package(source, Path(temporary_directory) / "destination")

            self.assertNotEqual(0, result.returncode)
            self.assertIn("Unexpected DLL", result.stdout + result.stderr)

    def test_rejects_missing_product_dll(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            source = Path(temporary_directory) / "source"
            source.mkdir()
            (source / PRODUCT_DLLS[0]).write_bytes(b"product")

            result = self.run_package(source, Path(temporary_directory) / "destination")

            self.assertNotEqual(0, result.returncode)
            self.assertIn(PRODUCT_DLLS[1], result.stdout + result.stderr)

    def test_rejects_unsupported_year(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            source = Path(temporary_directory) / "source"
            source.mkdir()
            for name in PRODUCT_DLLS:
                (source / name).write_bytes(b"product")

            result = self.run_package(source, Path(temporary_directory) / "destination", year="2025")

            self.assertNotEqual(0, result.returncode)
            self.assertIn("2026", result.stdout + result.stderr)


    def assert_inventory(self, directory: Path) -> subprocess.CompletedProcess[str]:
        command = f". '{INVENTORY}'; Assert-PreviewInventory '{directory}'"
        return subprocess.run(
            [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )

    def test_inventory_guard_rejects_hidden_entries_and_directories(self) -> None:
        # #235: every direct entry counts, hidden ones included.
        expected = (*PRODUCT_DLLS, "build-info.json", "new-proposal-preview.md", "new-proposal-smoke.scr")
        for extra, is_dir, hidden in (
            (None, False, False),
            (".scanner.tmp", False, True),
            ("scanner", True, False),
            (".scanner", True, True),
        ):
            with self.subTest(extra=extra), tempfile.TemporaryDirectory() as temporary_directory:
                package = Path(temporary_directory)
                for name in expected:
                    (package / name).write_bytes(b"x")
                if extra:
                    path = package / extra
                    path.mkdir() if is_dir else path.write_bytes(b"")
                    if hidden and os.name == "nt":
                        subprocess.run(["attrib", "+h", str(path)], check=True)

                result = self.assert_inventory(package)

                if extra is None:
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                else:
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(extra, result.stdout + result.stderr)
                    self.assertTrue(path.exists(), "the guard must not delete content")

    def test_rejects_hidden_unexpected_dll(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            source = Path(temporary_directory) / "source"
            source.mkdir()
            for name in PRODUCT_DLLS:
                (source / name).write_bytes(b"product")
            hidden = source / ".AcMgd.dll"
            hidden.write_bytes(b"must not ship")
            if os.name == "nt":
                subprocess.run(["attrib", "+h", str(hidden)], check=True)

            result = self.run_package(source, Path(temporary_directory) / "destination")

            self.assertNotEqual(0, result.returncode)
            self.assertIn("Unexpected DLL", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
