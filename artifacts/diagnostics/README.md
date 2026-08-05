| Version | Role | SHA-256 | Live Civil 3D status |
|---|---|---|---|
| 0.1.0 | Superseded original | `eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9` | Not run |
| 0.1.1 | Superseded; does not compile as-is | `ce9149fff4dd8a497218cf049abab73d48922946b4d933ad6f60987d0f50ac9b` | Not run as packaged |
| 0.1.2 | Current fixed source kit | `0f38f6c441bf36676da299dfd828dd8b801def8a8b55b63f60a4162747b506fa` | Not run as packaged |

`original/` preserves the supplied 0.1.0 source kit, `superseded/` the audited
0.1.1 replacement, and `current/` the 0.1.2 kit that carries the CS0104 fix. The
[diagnostic-kit audit](../../docs/diagnostics/diagnostic-kit-audit.md) records
static and package-validation evidence for 0.1.0 and 0.1.1.

The live-workstation gate closed on 2026-08-04 (local workstation time): a DLL
built from the in-tree source — 0.1.1 plus the fix below — ran
`AUTOGISDIAGNOSTICS` to a full PASS on a Civil 3D 2025 workstation. See the
[live-run evidence](../../docs/diagnostics/2026-08-04-live-run-civil3d-2025.md)
for build provenance and sanitized output.

**If you hold the 0.1.1 ZIP, replace it with 0.1.2.** 0.1.1 predates the CS0104
fix — `catch (Exception)` is ambiguous between `System.Exception` and
`Autodesk.AutoCAD.Runtime.Exception` — so any build from it fails against real
Autodesk assemblies. 0.1.2 carries the fix.

0.1.2 has not itself been built or loaded on a workstation. Its source is
identical to the 2026-08-04 live-PASS build except for the version strings and
line endings normalized per `.gitattributes`; it passed the kit's static and
Windows-wrapper validators from an extracted copy of the shipped ZIP.
