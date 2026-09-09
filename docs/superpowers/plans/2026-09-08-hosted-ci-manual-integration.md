# Hosted CI and manual Civil 3D integration implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the following bounded task under the owner's implementation direction. Steps use checkbox syntax; live status belongs on GitHub.

**Goal:** Deliver the 2026 preview DLLs from hosted CI on each push, with a manual licensed-host smoke handoff.

**Architecture:** Retain the pure Proposal core and single thin Adapter. Use official compile-only AutoCAD references and runtime Civil identity validation. CI builds the actual adapter; manual integration remains outside CI.

**Tech Stack:** Existing .NET 8/C#12/Windows Forms/xUnit, PowerShell, Python unittest and GitHub Actions.

**Spec:** [Hosted CI and manual integration design](../specs/2026-09-08-hosted-ci-manual-integration-design.md).

## Global constraints

- Only the 2026 target is configured; AutoCAD 25.1, Civil 13.8, x64.
- Pin Autodesk AutoCAD.NET/Core/Model to 25.1.0, use locked restore, exclude runtime assets and verify no Autodesk DLL ships.
- No standalone bridge, fake Autodesk API, source upload, self-hosted runner, or automatic license/native test.
- Preserve Proposal's Autodesk-free boundary, all established safety behavior, and the existing diagnostics/contract/fixtures.
- Creation stays disabled; manual native evidence and Phase 4 qualification are not implied by build success.

## Task 1: Build and hand off the actual 2026 preview from hosted CI

**Files:**
- Modify src/AutoGIS.Civil3D.Adapter/AutoGIS.Civil3D.Adapter.csproj, NewProposalCommand.cs and appropriate adapter tests.
- Modify Directory.Packages.props, affected packages.lock.json, .github/workflows/ci.yml and compat-autogis.yml.
- Add tools/package-preview.ps1 and tools/checks/tests/test_package_preview.py if an extracted packaging script makes the inventory/hash checks independently testable; otherwise use the smallest equivalently verified workflow step.
- Add scripts/new-proposal-smoke.scr and docs/new-proposal-preview.md.
- Add docs/adr/0009-hosted-ci-manual-integration.md (number allocated by root); update ADR index, ADR0008 supersession note, roadmap append-only decision log, governing accelerator/foundation design and original New Proposal plan where their CI/reference claims conflict. Link this design and plan rather than duplicating their requirements. Preserve historical decisions/evidence.

**Interfaces:** Keep ProposalPlanner, ProposalApproval and preview UI behavior unchanged. AUTOGISNEWPROPOSAL retains its name and disabled creation. New native command AUTOGISPROPOSALSMOKE reports host-binding status only. Civil3DYear defaults to 2026 and rejects other years. Package script accepts year, full commit, source build directory and destination directory, and emits a deterministic inventory plus build-info.json.

- [ ] Add meaningful regression checks: packaging rejects an extra AcMgd.dll; missing product DLL refuses upload; both expected product DLL hashes match; unsupported build year is refused; existing version/approval/core-boundary tests stay green. Use fresh temporary directories and synthetic file bytes only for packaging-unit cases, never fake SDK assemblies for compilation.

```powershell
# Controlled negative build; it must fail for an unsupported year.
dotnet build src/AutoGIS.Civil3D.Adapter -c Release -p:Civil3DYear=2025
```

- [ ] Replace installed SDK references with the official packages. Resolve their real restored assembly paths, preserve a negative path/reference probe if useful, and reject missing, wrong-name or wrong-series AutoCAD inputs. Confirm runtime assets do not propagate through the dependencies. Regenerate changed lock files once, then use locked restore.

```csharp
// Inside the existing guarded, non-inlined Civil identity method only.
return Type.GetType("Autodesk.Civil.ApplicationServices.CivilDocument, AeccDbMgd", throwOnError: true)!
    .Assembly.GetName().Version;
```

- [ ] Reuse the native active-document/version checks in both commands. The smoke path writes a clear host-binding PASS/FAIL and observed versions to the editor; it performs no drawing/database edits and creates no files. Keep missing/unloadable API failures actionable. No general dynamic Civil invocation API.

```text
AUTOGISPROPOSALSMOKE
AUTOGISNEWPROPOSAL
```

Save the script with a final newline. Instructions require NETLOAD first and explain that the second command opens a modal for manual checking/cancellation.

- [ ] Use the existing test job with a 2026 matrix and commit/year-specific artifact name. Preserve required checks and run unit tests before upload. Package only the two product DLLs, script, instructions and build-info.json; record actual commit and DLL SHA-256 values. Do not include pdb/deps/runtime Autodesk payloads accidentally through a broad wildcard. Keep compatibility validation scoped to its real Handoff.Cli project with locked restore/build.

- [ ] Record the owner's policy amendment under ADR0009 and append the roadmap decision. Replace contradictory active statements about installed SDK/dedicated-runner requirements; add scoped supersession notes to historical ADRs. Distinguish artifact availability, build-only merge eligibility, manual preview evidence and complete Phase 4 acceptance. Keep permanent target2026 and later phases unchanged.

- [ ] Run the complete relevant checks and the negative cases; inspect the produced local artifact and source diff. Then commit all coherent changes in one batch. Do not push until controller verification/review preparation.

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
dotnet test AutoGIS.Civil3D.sln -c Release --no-build
dotnet format AutoGIS.Civil3D.sln --verify-no-changes
python -m unittest discover -s tools/checks/tests
python tools/checks/docs_checks.py --baseline origin/main
python tools/agent-assets/sync.py --check
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_package.py
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_windows_scripts.py
git diff --check
```

Run the other Python CI suites with AGENT_SESSION_ID removed only in their subprocess. Controller independently reviews the exact pushed branch, obtains green hosted CI, downloads/inspects the resulting per-year artifact, and merges through protected GitHub workflow. Native preview issue stays open for the owner's manual run; no native acceptance is fabricated.
