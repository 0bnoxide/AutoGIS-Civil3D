# Hosted CI and manual Civil 3D integration — Design

**Status:** Approved direction from the owner on 2026-09-08: develop on the local Civil 3D 2026 trial installation; CI builds and unit-tests every push and produces per-year DLL artifacts; integration remains manual on the separately licensed work computer. Implementation choices below realize that direction under ADR-0009. This changes delivery of the existing Phase 4 preview, not the New Proposal creation requirements.

## Boundary

Keep the existing Autodesk-free Proposal core and thin Adapter. Do not create a bridge project or exclude the native adapter from CI. Planning, validation, action ordering and approval logic remain testable without starting Autodesk. All actual AutoCAD API calls stay in Adapter. No host, account, trial, or license activation is automated by CI.

The current preview uses CivilDocument only to obtain the bound Civil assembly version. Replace that compile-time type touch with a guarded runtime type lookup of `Autodesk.Civil.ApplicationServices.CivilDocument, AeccDbMgd`. Read the actual resolved assembly version and retain the AutoCAD 25.1 / Civil 13.8 / x64 guard. Missing, unloadable, or wrong-series Civil APIs must refuse the command. This is metadata lookup only, not a general reflection-based Civil API abstraction. Actual future Civil API operations still need supported APIs and an explicitly verified build-reference source.

## Build references

Use the Autodesk-owned NuGet packages AutoCAD.NET, AutoCAD.NET.Core and AutoCAD.NET.Model, each pinned centrally to 25.1.0 with committed lock files. These are the .NET 8 AutoCAD 2026 reference packages. Exclude runtime assets and keep references non-copying; verify actual output rather than treating PrivateAssets as Copy Local control. Retain a failing-capable build check for the three actual AutoCAD assembly names and 25.1 version series. Remove the installed SDK default and unused AecBaseMgd/AeccDbMgd compile dependencies. No 2025 fallback, third-party Civil package, fake Autodesk assembly, or uploaded installed Autodesk DLL.

Primary source: [Autodesk AutoCAD.NET 25.1.0](https://www.nuget.org/packages/AutoCAD.NET/25.1.0). Later package versions are not automatic upgrades. Native compatibility with a workstation's installed update is established by manual integration evidence.

## CI and artifacts

The ordinary Windows-hosted CI runs locked restore, build, unit tests, format and existing repository checks on every push and PR. The compatibility workflow builds only the handoff CLI it actually exercises, avoiding an irrelevant dependency on the UI/adapter; preserve its producer and validator legs. No self-hosted runner is required.

The build matrix initially contains only Civil 3D year 2026. Use a Civil3DYear build property defaulting to 2026, reject unconfigured years, and stamp that year in adapter assembly metadata. Additional years need actual matching reference pins and manual qualification; do not invent support for another year.

After successful build and tests, publish an Actions artifact named with year and exact checked-out commit. It contains only AutoGIS.Civil3D.Adapter.dll, AutoGIS.Civil3D.Proposal.dll, the manual smoke script, user-facing instructions, and build-info.json. Build info records year, exact commit, SHA-256 hashes of product DLLs, and the build-only qualification limit. Check the DLL inventory before packaging; unexpected DLLs, especially Autodesk DLLs, fail packaging. No workstation-specific paths, templates, user data, Autodesk binaries or CI credentials enter the artifact. This is a minimal preview handoff, not an installer or multi-release packaging project.

## Manual smoke handoff

Add AUTOGISPROPOSALSMOKE as a read-only command in the existing command class, reusing the native host guard. It prints an explicit PASS/FAIL for command/host binding, target year and observed API versions without changing drawings. Its PASS does not mean proposal creation or real-template qualification passed.

The delivered script runs AUTOGISPROPOSALSMOKE and AUTOGISNEWPROPOSAL after the user NETLOADs the product adapter DLL in an open disposable drawing on the licensed Civil 3D 2026 machine. The instructions describe downloading/extracting the year artifact, matching its commit, NETLOAD, SCRIPT, checking the modal preview and cancelling, then recording the artifact hash, host release/update and observed result. Do not change SECURELOAD, TRUSTEDPATHS, account or license settings. Company-template creation and timing remain governed by the New Proposal qualification requirements.

## Acceptance

A build-only preview change may merge after independent full review and green required hosted checks. Keep the native task open until manual evidence is supplied. Phase 4 acceptance still requires the complete workflow's native qualification; neither CI success nor a guard-only smoke passes that gate. The owner's separate work computer, not CI or an assumed permanent local license, is the integration host.

Verify clean hosted restore/build/unit tests without installed Autodesk, stable wrong-year/reference rejection, product-only artifacts with exact commit and hashes, and unchanged core boundary tests. Verify the smoke command's actual execution manually; do not substitute a fake host or offscreen form for that evidence.
