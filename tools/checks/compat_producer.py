"""Compat-harness producer leg (Phase 3 gate issue #78).

The pinned AutoGIS checkout's `autogis handoff` command (ADR-0128 in that
repository) emits a contract-v1 package from a LandXML extracted from the
fixture corpus, with explicit metadata including a known vertical datum.
The shipped validator must accept the emitted package with exit exactly 0
(zero warnings; per the spec the unknown-datum path is never gate
evidence). Requires the `autogis` package to be installed and AUTOGIS_PIN
to hold the pinned commit sha (recorded once in the compat workflow).
"""
import os
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "fixtures" / "v1" / "valid" / "known-vertical-datum.zip"
VALIDATOR = [
    "dotnet", "run", "-c", "Release", "--no-build",
    "--project", str(ROOT / "src" / "AutoGIS.Civil3D.Handoff.Cli"), "--",
]


def _run(label, cmd):
    try:
        return subprocess.run(
            cmd, capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=600,
        )
    except subprocess.TimeoutExpired as exc:
        print(f"FAIL: {label} -> timed out after 600s")
        if exc.stdout:
            print(exc.stdout)
        if exc.stderr:
            print(exc.stderr, file=sys.stderr)
        raise SystemExit(1) from None


def _fail(label, proc):
    print(f"FAIL: {label} -> exit {proc.returncode}")
    if proc.stdout:
        print(proc.stdout)
    if proc.stderr:
        print(proc.stderr, file=sys.stderr)
    return 1


def main():
    pin = os.environ.get("AUTOGIS_PIN", "")
    if not re.fullmatch(r"[0-9a-f]{7,64}", pin):
        print("FAIL: AUTOGIS_PIN must hold the pinned AutoGIS commit sha")
        return 1
    with tempfile.TemporaryDirectory() as tmp:
        source = Path(tmp) / "source.landxml"
        with zipfile.ZipFile(FIXTURE) as zf:
            source.write_bytes(zf.read("surface.landxml"))
        package = Path(tmp) / "producer-package.zip"
        producer = _run("producer emission", [
            sys.executable, "-m", "autogis", "handoff",
            "--input", str(source),
            "--output", str(package),
            "--vertical-unit", "metre",
            "--vertical-datum-authority", "EPSG",
            "--vertical-datum-code", "5703",
            "--vertical-datum-name", "NAVD88 height",
            "--source-commit", pin,
        ])
        if producer.returncode != 0:
            return _fail("producer emission", producer)
        print(f"ok: producer emitted a package at pin {pin[:12]}")
        verdict = _run(
            "validator on producer package", VALIDATOR + [str(package)])
        if verdict.returncode != 0:
            return _fail(
                "validator on producer package (want 0, zero warnings)",
                verdict)
        print("ok: validator accepted the producer package -> exit 0")
    print("compat_producer: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
