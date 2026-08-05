| Version | Role | SHA-256 | Live Civil 3D status |
|---|---|---|---|
| 0.1.0 | Superseded original | `eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9` | Not run |
| 0.1.1 | Current fixed source kit | `ce9149fff4dd8a497218cf049abab73d48922946b4d933ad6f60987d0f50ac9b` | PASS with in-tree fix, 2026-08-04 |

`original/` preserves the supplied 0.1.0 source kit; `current/` preserves the
audited 0.1.1 replacement. The [diagnostic-kit audit](../../docs/diagnostics/diagnostic-kit-audit.md) records static and package-validation evidence.

The live-workstation gate closed on 2026-08-04 (local workstation time): a DLL
built from the in-tree 0.1.1 source ran `AUTOGISDIAGNOSTICS` to a full PASS on
a Civil 3D 2025 workstation. See the
[live-run evidence](../../docs/diagnostics/2026-08-04-live-run-civil3d-2025.md)
for build provenance and sanitized output. Note: the archived 0.1.1 ZIP does
not compile as-is (CS0104 ambiguous `Exception`); the fix is in the in-tree
source and any future repack must include it.
