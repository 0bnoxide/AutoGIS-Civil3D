# Phase 4 New Proposal Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task after authorization. Use `superpowers:subagent-driven-development` only when delegation is authorized. Checkboxes describe execution steps; live progress and evidence belong on GitHub.

**Goal:** Deliver the approved `New Proposal` workflow inside Civil 3D, from deterministic preview through verified project creation and a creation receipt.

**Architecture:** A pure `AutoGIS.Civil3D.Proposal` library plans the work. A thin command in `AutoGIS.Civil3D.Adapter` collects input and approval; its executor owns filesystem changes and calls one native-host boundary. Reuse the existing result, command/presentation, build, and reference-boundary patterns without changing the handoff validator.

**Tech stack:** .NET 8, C# 12, System.Text.Json, existing xUnit packages, Windows Forms for the small native wizard, and the Civil 3D 2026 target under [ADR-0008](../../adr/0008-civil3d-2026-development-target.md). Native SDK calls and Sheet Set integration must be established by Task 3 probes before they become implementation commitments.

**Spec:** [Approved Civil Production Accelerator design](../specs/2026-09-04-civil-production-accelerator-design.md), under [ADR-0006](../../adr/0006-civil-production-accelerator.md).

**Decision state:** Approved by the owner 2026-09-04, recorded on [PR #102](https://github.com/0bnoxide/AutoGIS-Civil3D/pull/102). Implementation still requires the separate roadmap authorization change described below to merge first.

## Global constraints

The design owns requirements. Read its [workflow and release boundary](../specs/2026-09-04-civil-production-accelerator-design.md#decisions), [execution lifecycle](../specs/2026-09-04-civil-production-accelerator-design.md#execution-lifecycle), [acceptance evidence](../specs/2026-09-04-civil-production-accelerator-design.md#acceptance-evidence), [open owner inputs](../specs/2026-09-04-civil-production-accelerator-design.md#open-owner-inputs), and [exclusions](../specs/2026-09-04-civil-production-accelerator-design.md#exclusions). Those sections are not copied into this plan.

- The [roadmap](../../roadmap.md) owns implementation authority; later phases remain closed.
- The Proposal library performs no filesystem, clock, random-number, environment, network, or Autodesk calls. The adapter supplies observed facts and run identity.
- Preserve the contract, validator, CLI, fixture corpus, diagnostic kit, and diagnostic evidence. Reuse their patterns, not their mutable workflow or package-import seam.
- Follow the [adapter targeting and reference sourcing decisions](../specs/2026-09-04-phase-4-adapter-foundation-design.md#reference-assembly-sourcing) as qualified by ADR-0006 and ADR-0008. ADR-0008 replaces the single development target; a different reference source still needs its own owner decision.
- Use the existing central package versions and lock files. No mocking framework, service container, plugin framework, background service, or standalone launcher.
- Company conventions are manifest data. Synthetic test values must be clearly labeled and cannot be selected for a production run.
- A core or preview task is an enabling deliverable, not a completed production workflow. Do not enable the execution button until Tasks 3–5 pass.

## Authorization and delivery sequence

Before Task 1, obtain approval of this plan on its PR. Then merge a **documentation-only** authorization change that removes the Phase 4 marker and appends the owner's decision and evidence link to the roadmap gate-change log. Start implementation from a base containing that merged change. Do not remove the marker and add implementation in one PR: the [gate checks both baseline and current reservations](../specs/2026-08-14-phase-aware-documentation-gate-design.md#lifecycle).

Follow [the coordination lifecycle](../../collaboration.md) for each slice. Claim the branch, worktree, and paths before writing; keep `AGENT_SESSION_ID` set for real writes. Track execution on a Phase 4 delivery issue with child work items corresponding to these tasks. Do not allocate an ADR number in this plan; resolve [the consumed-number allocator defect](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/97) before the sourcing ADR required by the retained design is allocated.

Execute in order. Task 1 directly enables Task 2; together they reach a native, usable preview milestone. Task 3 proves the native operations. Tasks 4 and 5 deliver creation with verification and failure handling in the same user path. Task 6 qualifies that complete path. Do not ship intermediate native writers through an exposed command.

Each implementation PR receives the full code-review tier in [ADR-0004](../../adr/0004-one-adversarial-review-proportioned-to-risk.md): independent review and evidence on the exact pushed head, all findings dispositioned, and green CI. This proposed plan itself is documentation and follows the light tier.

## Existing code to read before execution

These are references, not a list of files to refactor:

| Pattern | Repository source | Application here |
|---|---|---|
| Immutable results and stable issue codes | `src/AutoGIS.Civil3D.Handoff/Validation/ValidationReport.cs`, `ValidationIssue.cs`, `IssueCodes.cs` | Use proposal-local records and codes; do not extend the frozen handoff code catalog |
| Staged, fail-closed orchestration | `src/AutoGIS.Civil3D.Handoff/BundleValidator.cs` | Validate before progressing; no partially valid execution plan |
| Command versus presentation | `src/AutoGIS.Civil3D.Handoff.Cli/CliApplication.cs`, `TextReportRenderer.cs` | Keep input, plan rendering, and execution distinct without a new framework |
| Autodesk-free boundary | `tests/AutoGIS.Civil3D.Handoff.Tests/ReferenceBoundaryTests.cs` | Add an independent Proposal boundary test and a negative detector control |
| Test project and version management | `tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj`, `Directory.Packages.props` | Reuse existing xUnit dependencies; do not import handoff fixture-builder dependencies |
| Native command and host guard | `diagnostics/AutoGIS.Civil3D.Diagnostics/src/AutoGIS.Civil3D.Diagnostics/DiagnosticsCommands.cs` | Adapt the modal command and active-document check in new product files |
| Assembly-series checks | `diagnostics/AutoGIS.Civil3D.Diagnostics/scripts/Build-Diagnostics.ps1` | Reuse the checked release-series logic, leaving the diagnostic script intact |

## File and interface map

Paths below are proposed implementation locations. Create them only when the task introduces their behavior; do not scaffold the entire map first. In the task sections, `Proposal/`, `Proposal.Tests/`, `Adapter/`, and `Adapter.Tests/` mean the following exact roots:

- `src/AutoGIS.Civil3D.Proposal/`
- `tests/AutoGIS.Civil3D.Proposal.Tests/`
- `src/AutoGIS.Civil3D.Adapter/`
- `tests/AutoGIS.Civil3D.Adapter.Tests/`

| Task | New files and responsibility | Existing files to modify |
|---|---|---|
| 1 | `Proposal/AutoGIS.Civil3D.Proposal.csproj`, `ProposalInputs.cs`, `StandardsManifest.cs`, `ProposalPlan.cs`, `ProposalPlanner.cs`, `ProposalIssue.cs`; `Proposal.Tests/AutoGIS.Civil3D.Proposal.Tests.csproj`, `ProposalPlannerTests.cs`, `StandardsManifestTests.cs`, `ReferenceBoundaryTests.cs`, `Fixtures/synthetic-standards.json` | `AutoGIS.Civil3D.sln`; generated lock files for the new projects |
| 2 | `Adapter/AutoGIS.Civil3D.Adapter.csproj`, `NewProposalCommand.cs`, `NewProposalForm.cs`, `ProposalPreview.cs`, `ProposalApproval.cs`, `HostCompatibility.cs`; `Adapter.Tests/AutoGIS.Civil3D.Adapter.Tests.csproj`, `PreviewTests.cs`, `HostCompatibilityTests.cs` | `AutoGIS.Civil3D.sln`, `Directory.Build.props`, `Directory.Packages.props`; affected lock files; `docs/architecture.md`; allocated sourcing ADR and its index row |
| 3 | `docs/diagnostics/new-proposal-native-probes.md` for sanitized procedure and interpretation; disposable probe code stays outside tracked product and diagnostic-kit files | The approved design only if an owner-approved clarification is needed |
| 4 | `Proposal/RunReceipt.cs`, `Proposal/PreflightReport.cs`, `Proposal/VerificationReport.cs`; `Adapter/ProposalExecutor.cs`, `IProposalHost.cs`, `ProposalFiles.cs`; `Adapter.Tests/ProposalExecutorTests.cs`, `RecordingProposalHost.cs` | `Proposal/ProposalPlan.cs` only for the verified run contract |
| 5 | `Adapter/NativeProposalHost.cs`, `DrawingWriter.cs`, `SheetSetWriter.cs`, `NativeProposalVerifier.cs`; `Adapter.Tests/NativePlanDispatchTests.cs` | `Adapter/NewProposalCommand.cs`, `NewProposalForm.cs`, and reference metadata if the proven Sheet Set API requires it |
| 6 | `docs/new-proposal.md`, `docs/diagnostics/new-proposal-qualification.md`; minimal product load artifact only within the approved Phase 4 packaging boundary | `README.md`, `docs/architecture.md`; roadmap only for a subsequent explicit owner acceptance decision |

The planner API is small; the records below define the handoff between slices. Keep constructors internal where needed and defensively copy collections so an approved plan cannot be modified in place.

```csharp
public sealed record ProposalInputs(
    string ClientName, string SiteName, int ProposalYear,
    string Orientation, string SheetSize,
    string? ClientNumber = null, string? ProjectNumber = null,
    string? ProposalNumber = null, string? SiteAddress = null,
    string? ProjectManager = null);

public sealed record ProposalIssue(string Code, string Message, string? Location = null);
public sealed record PlanResult(ProposalPlan? Plan, IReadOnlyList<ProposalIssue> Issues);

// Implemented in StandardsManifest.cs; invalid input returns no manifest.
public sealed record ManifestResult(StandardsManifest? Manifest, IReadOnlyList<ProposalIssue> Issues);
// StandardsManifest.Parse(ReadOnlySpan<byte> utf8) -> ManifestResult
// ProposalPlanner.Build(ProposalInputs inputs, StandardsManifest manifest) -> PlanResult
```

`StandardsManifest` is a versioned immutable model of the design's [manifest contract](../specs/2026-09-04-civil-production-accelerator-design.md#standards-manifest). `ProposalPlan` contains the final-root components, normalized inputs, manifest version, and ordered immutable `PlannedAction` values. It has a deterministic `ToJson()` representation for preview, comparison, and receipts. `PlannedAction` carries a stable action ID, an operation, a relative artifact path, dependency IDs, and typed data specific to that operation. Use a closed operation switch for folders, model drawings, sheet drawings, sheet-set/property operations, Xrefs, and support records; no reflection-driven dispatcher or arbitrary command strings.

Add `PreflightReport(IReadOnlyList<ProposalIssue> Issues)` in `Proposal/PreflightReport.cs` at Task 4; zero issues permits progression. `VerificationReport` contains verified relative artifact paths, failed checks, and explicit manual steps. `RunReceipt` contains the caller-supplied run ID and UTC time, plan identity, outcome, verified artifacts, manual steps, failed action ID, and cleanup/reporting failures. It never asserts native facts from a plan alone. These records contain no Autodesk types; collecting filesystem and native facts remains adapter work.

## Task 1: Produce a complete deterministic proposal preview

**Consumes:** Approved design, synthetic manifest bytes, and `ProposalInputs`.
**Produces:** `StandardsManifest.Parse`, `ProposalPlanner.Build`, `PlanResult`, immutable `ProposalPlan.ToJson()`; all are Autodesk-free and side-effect-free.

- [ ] Add the Proposal and Proposal.Tests projects to the solution using the existing test-project pattern. The core needs no new package dependency. Keep inherited .NET 8 settings, warnings-as-errors, and lock files.
- [ ] Write the parser's first failing tests in `StandardsManifestTests.cs`:

```csharp
[Theory]
[InlineData("{")]
[InlineData("{\"version\":1,\"version\":2}")]
[InlineData("{\"version\":999}")]
public void Invalid_or_ambiguous_manifest_has_no_value(string json)
{
    var result = StandardsManifest.Parse(System.Text.Encoding.UTF8.GetBytes(json));
    Assert.Null(result.Manifest);
    Assert.NotEmpty(result.Issues);
}
```

Run `dotnet test tests/AutoGIS.Civil3D.Proposal.Tests -c Release --filter StandardsManifestTests`; observe the failure before implementing the parser. Implement strict UTF-8 and JSON parsing with System.Text.Json, explicit supported-version checking, duplicate-property rejection at every object depth, and role/reference validation. Unknown or conflicting operative fields are errors. Put stable proposal-local codes in `ProposalIssue.cs`, with tests asserting codes rather than prose.

- [ ] Create `Fixtures/synthetic-standards.json` as a fully valid **test-only** manifest. Use a synthetic base root such as `C:/AutoGIS-Synthetic`, a `Model` folder, `Sheets` folder, `Support` folder, and `Shortcuts` folder; use the design's approved role graph. Use synthetic DWT paths and `TEST-01`, `TEST-02`, `TEST-03` sheet numbers. Label the fixture as synthetic in its test documentation. Populate all required manifest fields; no parser defaults may fabricate company conventions. Copy the fixture only to test output, never the product deliverable. The production form selects the owner's approved manifest; tests may supply their isolated fixture directly without a production bypass flag.
- [ ] Add `ProposalPlannerTests` cases for valid inputs, absent optional identifiers, deterministic output, ordinal action ordering, the entire manifest-derived artifact graph, and configuration/plan identity. A representative executable test is:

```csharp
[Fact]
public void Same_inputs_produce_identical_plans_without_template_access()
{
    var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "synthetic-standards.json"));
    var manifest = StandardsManifest.Parse(bytes).Manifest!;
    var inputs = new ProposalInputs("Test Client", "Test Site", 2026, "Landscape", "TEST-A1");
    var first = ProposalPlanner.Build(inputs, manifest);
    var second = ProposalPlanner.Build(inputs, manifest);
    Assert.Empty(first.Issues);
    Assert.NotNull(first.Plan);
    Assert.Equal(first.Plan!.ToJson(), second.Plan!.ToJson());
}
```

The fixture must declare `Landscape`/`TEST-A1`; its template paths intentionally do not exist. The test reads the fixture; the planner must not read templates or the filesystem.

- [ ] Add table-driven input/path cases: missing names, whitespace-only values, unsupported size/orientation, invalid year, `..`, rooted or drive-relative components, UNC injection, separators, alternate data streams, reserved Windows device names, trailing dots/spaces, control characters, and case-insensitive output collisions. Do not silently repair ambiguous names. Validate relative artifact paths against a Windows path policy even when tests run on another OS; derive the display root from manifest data and inputs without checking existence. Include duplicate roles, missing references, cycles, and collisions between folders and files.
- [ ] Implement the smallest ordered planner. Test that `ProposalPlanner.Build(inputs with { ClientNumber = "TEST-CLIENT-1" }, manifest)` preserves the first plan's `FinalRoot` and artifact paths while updating the relevant planned metadata. Include all support-record actions and explicit EG-pending state from the design. Do not introduce a CLI merely to display it.
- [ ] Add a Proposal reference-closure test using the existing detector pattern and negative controls; also verify that the new core project has no Autodesk, Esri, Adapter, or Handoff project dependency. Run all Proposal tests on a machine without Autodesk.
- [ ] Run the common checks below and commit this tested slice. This slice directly unblocks Task 2; do not advertise it as a completed native workflow.

## Task 2: Run the proposal preview inside Civil 3D

**Consumes:** Task 1's parser and planner.
**Produces:** Modal `NewProposalCommand.Run()` registered as `AUTOGISNEWPROPOSAL`; a wizard and `ProposalPreview.Write(ProposalPlan plan, TextWriter output)` that display the same plan later executed. `ProposalApproval` is an immutable snapshot of that preview's canonical plan JSON and expected manifest/template fingerprints, with defensively copied collections. No creation command is exposed yet.

- [ ] In the new adapter project, use `net8.0-windows`, x64, and the built-in Windows Forms support. Refactor only the target-framework assignment in `Directory.Build.props` to permit an explicit project target while preserving all common settings and diagnostic exclusions. Verify the effective target with `dotnet msbuild -getProperty:TargetFramework` for the adapter and existing handoff project. Do not create a second general-purpose build system.
- [ ] Resolve and pin matching reference packages for ADR-0008's target, recording exact versions and provenance in the sourcing decision. Add non-copying references and locked restore. Adapt the existing assembly-series checks into the adapter build: an intentionally mismatched reference must fail. Confirm no Autodesk DLL is copied to the deliverable. If matching packages are unavailable, stop dependent build changes and record the blocker on the delivery issue; do not use the historical 2025 pins, weaken checks, or switch sources without a separate owner decision.
- [ ] Add `HostCompatibilityTests` for ADR-0008's target series and mismatched series. Add `PreviewTests` asserting that every action, final target, relevant input, and manual limitation appears once and in plan order. Keep the renderer free of Autodesk types so ordinary CI can exercise it.

```csharp
// The renderer consumes an already validated Task 1 plan.
using var output = new StringWriter();
ProposalPreview.Write(plan, output);
Assert.Contains(plan.FinalRoot, output.ToString(), StringComparison.Ordinal);
foreach (var action in plan.Actions)
    Assert.Contains(action.Id, output.ToString(), StringComparison.Ordinal);
```

Here `plan` is the valid result constructed with the Task 1 fixture and input expression; put that construction directly in `PreviewTests`, not in a new shared test library. Define `FinalRoot` and `Actions` on `ProposalPlan` in Task 1.

- [ ] Implement the command's active-document and host checks using the diagnostic command as a read-only reference. The form collects the design's inputs, loads the selected standards manifest, and presents a readable plan. Capture the canonical plan JSON and expected SHA-256 fingerprints of the manifest and every selected template when constructing that preview, before the approval control can be used. A change to input, manifest, or template invalidates the preview and approval; closing the form or cancelling produces no output files. Display `Execution unavailable in preview build` while execution is unimplemented, and disable its control.
- [ ] In `ProposalApproval.cs`, expose `PlanJson` and a read-only `DependencyFingerprints` path-to-digest map. Keep construction inside adapter orchestration and snapshot the values once; explicit approval passes that existing preview snapshot to the executor, never freshly captured expectations. Add tests for mutation attempts on the source collections, changed inputs, and manifest/template replacement after preview but before approval. The core still performs no filesystem or hashing calls.
- [ ] Exercise the actual command in ADR-0008's target host: missing active drawing, valid preview, invalid inputs, unsupported host, cancel, and edited input after preview. Compare filesystem snapshots before/after; there must be no proposal or staging artifacts. Record a timed comparison against manually determining the same setup actions. If the native host is unavailable, retain a build-only result and leave this milestone unaccepted.
- [ ] Run ordinary adapter tests without Autodesk installed; if assembly discovery eagerly loads Autodesk types, separate the host entry point from the tested classes inside the same adapter project. Do not add a fake Autodesk assembly. Run the common checks, then commit the preview slice and publish the live evidence on its issue.

## Task 3: Prove native operations before committing their implementation

**Consumes:** Task 2's loadable adapter, a disposable drawing workspace, and owner inputs when their corresponding operation is reached.
**Produces:** A sanitized decision record in `docs/diagnostics/new-proposal-native-probes.md`, with exact release, APIs, reference provenance, observed outputs, and failure behavior. Experimental code is disposable and does not become a production subsystem.

- [ ] Ask the design's open owner inputs one at a time at the point of use. The folder/template/profile and sheet naming inputs gate real creation; title-block/page-setup and placeholder inputs gate the sheet writer; Sheet Set Manager mode gates the relevant host probe on ADR-0008's target. Synthetic inputs unblock experiments, not claims about company compatibility. Link to the design's input list rather than copying it into a new tracker.
- [ ] For each operation in the design's [required spikes](../specs/2026-09-04-civil-production-accelerator-design.md#implementation-boundary), write down the concrete hypothesis and a pass/fail observation before running it. Use official Autodesk documentation for the actual release and record the API members exercised; do not infer supported automation from UI availability. Test DST property updates and title-block field refresh, DWT-layout selection, relative overlay behavior after directory relocation, and handle release before renaming the staging root.
- [ ] Prove the close/verify/close cycle: create and save disposable artifacts, close every created handle, reopen for independent inspection, close inspection handles, rename the parent, then open all outputs at their new location. Compare Xrefs and sheet registration after relocation. A successful save alone is insufficient evidence.
- [ ] Determine whether the required Sheet Set operations need additional interop references and how they are supplied legally in CI and on the supported host. Record the smallest proven reference setup. Keep network project provisioning and other Sheet Set modes outside the slice. If a required operation fails, stop its dependent task and file the blocker; only the design's explicit data-shortcut manual fallback is available without a design change.
- [ ] Resolve the [receipt-publication failure boundary](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/101) **before Task 4**. The design writes the receipt after promotion but also requires failed runs to leave no final root. Probe an unwritable receipt destination after a successful directory move. Proposed clarification for owner review: prepare and flush the complete success receipt inside staging, publish it with the verified root, then display it; a display failure preserves the completed project. Record approval and amend the design before implementing that ordering. If the owner retains post-promotion receipt writing, record an equally explicit recovery policy first. This plan does not silently change the approved lifecycle.
- [ ] Use the existing load procedure for experiments. Generic installers, signing, multi-release distribution, and a new packaging system remain later-phase work. Record the minimal load artifact needed for Phase 4 qualification; obtain an explicit scope decision if it would cross that boundary.
- [ ] Commit only the sanitized probe procedure and decisions, with links to evidence on the delivery issue. Required observations must pass before the related production code is written. These probes are not substitutes for final qualification.

## Task 4: Make execution and failure handling testable without Autodesk

**Consumes:** Immutable plan and Task 2's approved preview snapshot; Task 3's lifecycle decision; a caller-provided run ID and time.
**Produces:** `ProposalExecutor.Run` and a receipt backed by observed results. `ProposalFiles` is adapter-local filesystem code; `IProposalHost` is the sole fakeable native boundary.

```csharp
public interface IProposalHost
{
    PreflightReport Inspect(ProposalPlan plan);
    void Apply(PlannedAction action, string stagingRoot);
    void CloseCreatedArtifacts();
    VerificationReport Verify(ProposalPlan plan, string artifactRoot);
}
// ProposalExecutor.Run(ProposalPlan plan, ProposalApproval? approval, IProposalHost host,
//     Guid runId, DateTimeOffset startedAt, string failureReceiptDirectory) -> RunReceipt
```

The executor constructor takes no service container. `RecordingProposalHost` implements the interface in the test project, logs calls, writes synthetic artifact bytes under the supplied temporary root, and can fail at a selected action ID, close, or verification. Its behavior is a test double, never production completion evidence.

- [ ] Write failing `ProposalExecutorTests` using an OS temporary directory and the Task 1 fixture remapped to that directory. Start with approval refusal and existing-target refusal; assert byte-for-byte unchanged existing contents and no host mutation calls. Then inject failure at every action boundary, close, verification, promotion, and the receipt boundary selected in Task 3. Assert final-root outcome, retained failure receipt, exact failed action, and visible cleanup failure.

```csharp
// Execute each case with a fresh temporary workspace and RecordingProposalHost.
var result = executor.Run(plan, null, host, Guid.NewGuid(), fixedTime, failureDirectory);
Assert.Equal("Cancelled", result.Outcome);
Assert.Empty(host.Mutations);
Assert.False(Directory.Exists(plan.FinalRoot));
```

`executor` is a `new ProposalExecutor()`, `fixedTime` is `DateTimeOffset.Parse("2026-09-04T00:00:00Z")`, and `failureDirectory` is a sibling of the plan's final root inside the test temporary directory. `Mutations` is the test double's action/close mutation log. Set `RunReceipt.Outcome` to a documented closed string set (`Cancelled`, `Refused`, `Failed`, `Succeeded`) and assert successful receipts only after actual verification and publication.

- [ ] Implement preflight: check final-root absence, root containment, readable required templates, writable approved locations, and native `Inspect` results before creating proposal artifacts. Recheck relevant facts immediately before mutation. Reject reparse points in target/staging paths or their traversed ancestors; use case-insensitive Windows containment with a separator boundary. All leaf creation is exclusive; target races fail rather than merge. Preserve foreign files even when cleanup would otherwise be convenient.
- [ ] Reserve a unique sibling staging directory, record ownership, and execute only its planned actions. Never adopt an existing staging directory. Track created handles and ensure close is attempted on success and failure. Verify using the host's independent readback, close verification handles, and compare its observations with the plan. Only then promote without overwrite, using Task 3's approved receipt-publication sequence.
- [ ] Require `plan.ToJson()` to match `approval.PlanJson` and compare current dependencies against `approval.DependencyFingerprints` before mutation. Never replace the expected fingerprints with values captured when `Run` starts. Test manifest/template changes both after preview but before approval and after approval but before execution: each refuses execution, creates no staging/final artifacts, and requires a new preview and explicit approval. Revalidate at the native use boundary or consume the exact verified bytes so a later replacement cannot silently change the approved content. Keep fingerprints in adapter orchestration, not in the pure planner. Restrict cleanup to the exact owned root and known generated entries; report unexpected entries or locked handles instead of broad recursive deletion.
- [ ] Store failure receipts outside the disposable root, without overwriting an earlier receipt. Report receipt-storage failure to the user and retain the diagnostic context; never report success merely because exception handling completed. Test absence of write permission, process-interruption leftovers and safe refusal on rerun, target races, locked files, and foreign staging entries. No repair/resume mode is added.
- [ ] Run `dotnet test tests/AutoGIS.Civil3D.Adapter.Tests -c Release --filter ProposalExecutorTests` until the real filesystem and fake-host cases pass. Run the common checks and commit the executor with its tests. Leave execution disabled in the command until Task 5 passes.

## Task 5: Create and independently verify native proposal artifacts

**Consumes:** Task 4's executor, immutable actions, and Task 3's demonstrated APIs.
**Produces:** `NativeProposalHost : IProposalHost`; `DrawingWriter`, `SheetSetWriter`, and `NativeProposalVerifier` implement the native operations without changing the executor's lifecycle.

- [ ] Write action-dispatch tests proving that the writer receives each action once, in dependency order, and only beneath the supplied staging root. Reject unknown operations. Bind the proven native methods behind that closed dispatch; do not use command-string automation where a supported API was proven. The real drawing and Sheet Set implementation stays on the required host thread and uses the proven transaction/handle pattern.
- [ ] Implement model and sheet creation from the selected templates, then DST registration/property updates, relative overlay references, placeholder creation, and support records from the plan. Follow the [design's drawing and sheet rules](../specs/2026-09-04-civil-production-accelerator-design.md#drawings-references-and-sheets); neither the writer nor the form introduces its own company defaults. Record the proven data-shortcut outcome, including an explicit manual step when the design permits it.
- [ ] Implement `Verify` as readback of saved native artifacts, not a replay of writer inputs. Compare drawing/template-derived settings, layouts, placeholders, sheet registrations, properties, and the entire Xref graph with the plan. Detect missing, duplicate, absolute-path, attached-versus-overlay, and unexpected references. Read only artifacts created for this run and close all handles afterward.
- [ ] Run each negative native probe on disposable generated output: remove a required reference, alter a sheet registration, change a mapped property, and lock one created drawing. For each, assert that verification refuses publication and the executor reports the specific failed check. Check that the user's pre-existing active drawing and templates remain unchanged.

```csharp
// Run inside the qualified native test host on deliberately altered staged output.
VerificationReport report = nativeHost.Verify(plan, stagingRoot);
Assert.NotEmpty(report.FailedChecks);
Assert.False(Directory.Exists(plan.FinalRoot));
```

`nativeHost` is `NativeProposalHost`, and `stagingRoot` is the owned root produced by the executor's native test harness. `VerificationReport.FailedChecks` is an immutable list of proposal issues. This is a native test, not an ordinary CI assertion against reference-only assemblies.

- [ ] Wire the form's approval control to `ProposalExecutor.Run` only after the native success and failure path has passed. Show the same plan identity used for execution. On refusal or failure, display the exact operation and receipt location; on success, show the verified root, receipt, and any recorded manual work. Do not label a manual data-shortcut association as automated completion.
- [ ] Add the supported, explicit procedure for adding a client number later: update the Civil-local configuration and mapped DST properties, refresh linked fields, and verify that the root path is unchanged. This is a documented metadata-only user procedure, not a rerun of `New Proposal` against an existing target or a new automated repair command. Demonstrate it in Task 6.
- [ ] Run the common checks and record native evidence before committing and requesting full review. The complete create/verify/receipt path is now eligible for qualification; it is not yet a supported release.

## Task 6: Qualify the complete workflow and hand it over

**Consumes:** The reviewed implementation, answered owner inputs, and real approved templates on ADR-0008's target host.
**Produces:** Reproducible qualification instructions, sanitized evidence, and an owner acceptance request. Only the owner advances the roadmap.

- [ ] Write `docs/new-proposal.md` for a Civil designer: prerequisites, selecting standards, invoking the command, reviewing the plan, approving/cancelling, understanding success or failure receipts, and the metadata-only update procedure. Link to the versioned standards format owned by `StandardsManifest.cs`; do not duplicate company configuration values in prose.
- [ ] In `docs/diagnostics/new-proposal-qualification.md`, provide a result table keyed to the design's acceptance requirements, with columns for procedure, expected observable result, actual evidence link, release/build, and owner acceptance. Actual execution results belong on the gate issue; the document owns the reusable procedure. Cover successful creation, changed inputs, repeat-target refusal, forced mid-run failure, cleanup/receipt failure, and later client-number addition.
- [ ] Measure a manual baseline and the automated workflow on the same representative proposal. Include input/preview time and record the timing boundaries. Demonstrate the design's time target with the owner's real templates and obtain the owner's confirmation of meaningful savings. Record manual fallback effort separately.
- [ ] Run the artifact checks after relocation to the final root and after a fresh Civil 3D session; verify sheet-set registration, fields, and references persist. Prove the load artifact contains only redistributable product files, and preserve the diagnostic kit. Qualify only releases actually run; a build matrix is not a support claim.
- [ ] Attach exact commit, release, template/manifest identifiers, sanitized observations, receipts, failure outcomes, and timing to the Phase 4 gate issue. Address the [Sonar analysis blocker](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/96) or obtain an explicit documented disposition for the production-code gate; a cancelled analysis is not a successful security analysis.
- [ ] Obtain full review and green CI on the final implementation head. Request owner acceptance with the design-to-evidence mapping. Any accepted gate change is a separate owner decision recorded through the roadmap lifecycle; do not automatically open Phase 5 or begin the next workflow.

## Common validation and commit procedure

For each code slice, first run its focused tests and observe a meaningful failure, implement the smallest change, then run focused tests and the full applicable checks. Generate new lock files once when introducing project dependencies; all subsequent validation uses locked restore. Record actual counts and hashes, never the expected counts from an earlier task.

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
dotnet test AutoGIS.Civil3D.sln -c Release --no-build
dotnet format AutoGIS.Civil3D.sln --verify-no-changes
python tools/checks/docs_checks.py --baseline origin/main
python tools/agent-assets/sync.py --check
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_package.py
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_windows_scripts.py
git diff --check
```

Run the Python suites from `.github/workflows/ci.yml` as well. Until [issue #100](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/100) is fixed, remove `AGENT_SESSION_ID` only in the test subprocess, preserving it for coordination and writes:

```python
import os, subprocess, sys
test_env = os.environ.copy()
test_env.pop("AGENT_SESSION_ID", None)
for suite in ("agent-coordination", "checks", "agent-assets", "pr-monitor", "agent-hooks"):
    subprocess.run([sys.executable, "-m", "unittest", "discover",
                    "-s", f"tools/{suite}/tests"], env=test_env, check=True)
```

Before each commit, run the coordination `check` for the active session, inspect the complete diff and secret-scan changed files, and stage only the paths owned by that task. Suggested commits in task order: `feat: plan new proposals from validated standards`; `feat: preview new proposals in Civil 3D`; `docs: record new proposal native API probes`; `feat: execute proposal plans with verified staging`; `feat: create and verify native proposal artifacts`; `docs: qualify and document new proposal workflow`. Split further only when a smaller change has its own meaningful test cycle; do not separate safety checks from the mutation they protect.

## Coverage and handoff checks

| Design requirement family | Implementation and proof |
|---|---|
| Manifest, inputs, paths, deterministic planning, artifact roles | Task 1 parser/planner cases and Proposal boundary tests |
| Native command, input collection, preview, explicit approval | Task 2 live preview and Task 5 wired approval path |
| DWG/DWT, sheets, DST, properties, placeholders, Xrefs, support records | Task 3 API evidence and Task 5 native readback |
| Preflight, staging, ownership, no overwrite, cleanup, receipts | Task 4 real-filesystem fault tests plus Tasks 3 and 5 native handle/verification evidence |
| Later client-number addition without root rename | Task 5 metadata procedure, demonstrated in Task 6 |
| Data shortcuts and explicit permitted manual fallback | Task 3 feasibility decision, Task 5 receipt, Task 6 timing and qualification |
| Build/reference sourcing, no redistributed Autodesk assemblies | Task 2 negative series check and Task 6 load-artifact inspection |
| Real templates, supported release, time target, owner acceptance | Task 6 evidence on the Phase 4 gate issue |

Before publishing an execution slice, check its interfaces against this map and its requirements against the governing design. If a probe or owner input changes the required behavior, update the owning design and this plan through review before changing production code. Do not silently downgrade a failed native requirement into a passing fake test.
