"""Compat-harness smoke: the shipped validator CLI, invoked as a subprocess
exactly as the compat workflow invokes it, discriminates clean / warning /
invalid packages by exit code (0 / 2 / 1 per CliApplication.cs).

Phase 3 gate evidence requires exit exactly 0 (zero warnings) for the
producer package; the warning and invalid legs prove that requirement can
fail, so a green run is meaningful (spec: negative control).
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLI = [
    "dotnet", "run", "-c", "Release", "--no-build",
    "--project", str(ROOT / "src" / "AutoGIS.Civil3D.Handoff.Cli"), "--",
]

CASES = (
    ("fixtures/v1/valid/known-vertical-datum.zip", 0),
    ("fixtures/v1/valid/unknown-vertical-datum.zip", 2),
    ("fixtures/v1/invalid/checksum.zip", 1),
)


def main():
    failures = 0
    for rel, expected in CASES:
        try:
            proc = subprocess.run(
                CLI + [str(ROOT / rel)], capture_output=True,
                text=True, encoding="utf-8", errors="replace", timeout=600,
            )
        except subprocess.TimeoutExpired as exc:
            failures += 1
            print(f"FAIL: {rel} -> timed out after 600s")
            if exc.stdout:
                print(exc.stdout)
            if exc.stderr:
                print(exc.stderr, file=sys.stderr)
            continue
        ok = proc.returncode == expected
        failures += not ok
        print(f"{'ok' if ok else 'FAIL'}: {rel} -> exit {proc.returncode}"
              f" (want {expected})")
        if not ok:
            if proc.stdout:
                print(proc.stdout)
            if proc.stderr:
                print(proc.stderr, file=sys.stderr)
    if failures:
        return 1
    print("compat_smoke: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
