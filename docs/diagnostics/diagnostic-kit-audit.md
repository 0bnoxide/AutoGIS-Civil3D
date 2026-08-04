# AutoGIS Civil 3D diagnostic-kit audit

**Date:** 2026-08-02
**Original artifact:** `AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip`
**Original SHA-256:** `eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9`
**Fixed artifact:** `AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip`
**Fixed SHA-256:** `ce9149fff4dd8a497218cf049abab73d48922946b4d933ad6f60987d0f50ac9b`

## Conclusion

The original archive remains intact and still matches the handoff hash. Version
`0.1.1` resolves the reproduced Windows PowerShell wrapper failure, expands the
read-only guard to every C# source file, removes the ambiguous preliminary
`PASS`, adds an in-command privacy warning, and makes installation staged,
hash-verified, collision-resistant, and rollback-capable.

The fixed source kit passed static, parser, real CMD-wrapper, temporary-profile,
and post-packaging archive validation. It is ready for the authorized IT/CAD
workstation validation steps below. It is still a source kit, not a compiled or
live-tested Civil 3D plug-in; this machine has neither a .NET 8 SDK nor Civil 3D
2025, so no claim is made yet about `NETLOAD` or live command execution.

## Evidence completed

- Opened and streamed every ZIP member successfully.
- Confirmed 24 archive entries and 30,753 uncompressed bytes.
- Found no rooted paths, traversal paths, duplicate paths, or symbolic links.
- Matched the expected SHA-256 exactly.
- Extracted a separate review copy under `diagnostic-kit-review/`; the source
  ZIP remains unchanged.
- Read every C#, project, manifest, PowerShell, CMD, Python, and Markdown file.
- Ran `python tests/validate_package.py`: passed.
- Parsed all PowerShell scripts without executing them: passed.
- Scanned all runtime C# for file, registry, network, process, transaction,
  save, erase, system-variable-write, and write-mode tokens: no implementation
  hit found.
- Verified the official Autodesk requirements used by the kit:
  `.NET 8`, `x64`, the five base managed references with Copy Local disabled,
  and an `R25.0`/`Civil3D`/`Win64` bundle target.
- Confirmed this machine has neither a .NET 8 SDK nor a Civil 3D 2025
  installation. `build.cmd` failed before creating `bin`, `obj`, or a DLL.
- Exercised install, repeat-install backup, and uninstall against redirected
  temporary profile directories under `C:\tmp`: direct PowerShell execution
  passed and preserved the installed bundle in recoverable backups.
- Confirmed the downloaded ZIP has a `Zone.Identifier` stream and all included
  PowerShell scripts are unsigned.
- Replaced the module-dependent hash command with a local
  `System.Security.Cryptography.SHA256` helper and ran the actual CMD wrappers
  through Windows PowerShell: passed.
- Exercised initial install, two immediate reinstalls, two unique recoverable
  backups, fail-closed missing-manifest preflight, uninstall, a third unique
  backup, and no-op uninstall in an isolated temporary profile: passed.
- Parsed every fixed PowerShell script and reran the expanded all-C# static
  validation: passed.
- Created `AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip` without changing the
  original archive. The new archive contains 26 entries/17 files and no DLL,
  PDB, build, cache, rooted, traversal, duplicate, or symbolic-link content.
- Reopened and streamed every new archive member, matched every file hash to the
  fixed source tree, safely extracted it under a unique `C:\tmp` directory, and
  reran both validators from that extracted copy: passed.

## Findings

### 1. Blocker: CMD entrypoints can fail after their side effect

**Status: resolved in 0.1.1.** Build and install now use the local SHA-256
helper. The Windows regression test invokes the shipped `.cmd` wrappers and
passes even in the module-path environment that reproduced the failure.

`build.cmd`, `install-current-user.cmd`, and `uninstall-current-user.cmd` force
Windows PowerShell through `powershell.exe`. Both the build and install scripts
then call `Get-FileHash` without explicitly loading the compatible utility
module.

In the current environment, PowerShell launched through CMD sees a PowerShell 7
utility module before the Windows PowerShell module. Command discovery cannot
load `Get-FileHash`. This was reproduced twice:

- `install-current-user.cmd` copied the bundle successfully;
- `Install-CurrentUser.ps1` then failed at its hash-reporting line;
- the wrapper returned exit code 1 despite an installed target being present.

The build script has the same dependency after it compiles and copies the DLL,
so it can report failure after producing valid output.

**Required correction:** use a local SHA-256 helper based on
`System.Security.Cryptography.SHA256`, or explicitly import the utility module
from the running host's `$PSHOME`. Add regression coverage that invokes the CMD
wrappers through Windows PowerShell rather than only parsing the scripts.

### 2. Safety validation covers only one C# file

**Status: resolved in 0.1.1.** Static validation enumerates every runtime `.cs`
file and applies the expanded read-only token guard to each one.

`tests/validate_package.py` scans forbidden tokens only in
`DiagnosticsCommands.cs`. It does not inspect `Plugin.cs` or future C# files.
The current `Plugin.cs` is empty and safe, but the test would not detect a
future load-time file, process, registry, or network side effect added there.

**Recommended correction:** scan every runtime `.cs` file and keep the manual
review requirement for Autodesk calls that a token scan cannot classify.

### 3. The output emits `PASS` before all sections finish

**Status: resolved in 0.1.1.** The preliminary event is now `LOAD: PASS`; the
final `RESULT` remains the only overall `PASS`/`PARTIAL` result.

The command writes `PASS: The managed plug-in loaded...` before executing the
five guarded sections, then can finish with `RESULT: PARTIAL`. That is readable
to a person but ambiguous for evidence parsing and screenshots.

**Recommended correction:** label the early event `LOAD: PASS` or
`COMMAND: STARTED`; reserve unqualified `PASS` for the final result.

### 4. Diagnostic evidence can expose workstation or project paths

**Status: mitigated in 0.1.1; operating control remains required.** The command
now emits a `PRIVACY` warning, and both pilot documents require a sanitized
drawing and path redaction before external sharing.

The output includes the plug-in path, raw drawing name/path, and
`TRUSTEDPATHS`. Those can disclose a username, client/project naming, local
folders, or network-share locations when logs are pasted into an external
system.

**Required operating control:** use a blank or sanitized drawing and redact
paths before sharing output. Preserve an unredacted copy only in an approved
internal evidence location.

### 5. Installer backup naming and rollback need hardening

**Status: resolved in 0.1.1.** The installer validates linked-path hazards,
stages and hashes the manifest and DLL before touching the active target, uses
millisecond-plus-random backup names, verifies the activated copy, preserves a
failed new copy when possible, and restores the prior bundle after failure.
Uninstall uses the same collision-resistant naming helper.

Backup names have one-second timestamp resolution. Closely repeated operations
can collide. The installer also moves the prior bundle before copying and
verifying the replacement; a later failure leaves the previous version only in
the backup directory and may leave a partial target.

This is acceptable for a supervised one-user pilot only after the blocker above
is fixed. Before broader use, create a unique backup name, stage and verify the
new bundle, and restore the previous bundle automatically on failure.

### 6. Download and signing policy remains an expected IT gate

**Status: unchanged external gate, now documented more explicitly.** The README
and IT notes say to capture the exact policy block and prohibit `Unblock-File`,
execution-policy bypass, or weakening application-control settings.

The ZIP is marked as downloaded from the internet and the PowerShell scripts
are unsigned. Windows execution policy, Defender, AppLocker, WDAC, or company
controls may block the scripts or the compiled DLL. Do not remove or bypass
those controls merely to make the pilot pass; capture the exact block and ask
CAD/IT for the approved build, signing, and deployment route.

## Validation still required on the workstation

The source-level corrections and temporary-profile checks are complete. On the
authorized Civil 3D 2025 workstation:

1. Confirm `dotnet --list-sdks` reports an approved 8.x SDK.
2. Build against the installed Civil 3D 2025 assemblies.
3. Capture the resolved assembly paths, assembly versions, file versions, build
   output, and DLL hash.
4. Use `NETLOAD` with a blank or sanitized drawing.
5. Run `AUTOGISDIAGNOSTICS` and retain both redacted shareable evidence and the
   approved internal original.
6. Do not change `SECURELOAD`, `TRUSTEDPATHS`, endpoint protection, or
   application-control policy to force success.
