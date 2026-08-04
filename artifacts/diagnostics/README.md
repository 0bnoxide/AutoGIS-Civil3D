| Version | Role | SHA-256 | Live Civil 3D status |
|---|---|---|---|
| 0.1.0 | Superseded original | `eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9` | Not run |
| 0.1.1 | Current fixed source kit | `ce9149fff4dd8a497218cf049abab73d48922946b4d933ad6f60987d0f50ac9b` | Awaiting authorized workstation |

`original/` preserves the supplied 0.1.0 source kit; `current/` preserves the
audited 0.1.1 replacement. The [diagnostic-kit audit](../../docs/diagnostics/diagnostic-kit-audit.md) records static and package-validation evidence.

Neither artifact proves a live Civil 3D result. The remaining gate is to build
and load 0.1.1 on an authorized Civil 3D 2025 workstation, run
`AUTOGISDIAGNOSTICS` using a blank or sanitized drawing, and retain sanitized
evidence without weakening workstation policy.
