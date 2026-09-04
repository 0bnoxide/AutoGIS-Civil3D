# Civil Production Accelerator, Phase 4 `New Proposal` — Design

**Status:** Proposed 2026-09-04. Governs roadmap Phase 4 (authorized
2026-08-13, [gate-change log](../../roadmap.md)) under
[ADR-0006](../../adr/0006-civil-production-accelerator.md). Approval is an
owner decision recorded on the pull request that merges this document.
Implementation stays blocked until the separate Phase 4 implementation plan
is approved and the roadmap marker this design reserves is removed, per the
[phase-aware documentation gate](2026-08-14-phase-aware-documentation-gate-design.md).

## Problem

The repository can prove a package conforms to a contract. It cannot yet do
anything a civil designer would notice. Setting up a new proposal project by
hand — folder tree, three model drawings from a template, a sheet set, three
starter sheets, the cross-reference graph between them, and the metadata
that reaches title blocks — is repetitive, error-prone, and done often
enough that the errors are expensive. Nothing in the accepted phases
addresses it, and the capability that would have come next serves an import
path no production workflow requires.

`New Proposal` is the first workflow chosen to change that: it is bounded,
it writes only files the run creates, it needs no design intelligence, and
its correctness is checkable by inspecting the files it produced.

## Decisions

### Workflow sequence

The production foundation delivers user-facing vertical slices in order:
`New Proposal`, `Promote Project`, `Intake Source Files`, `Survey
Preflight`, `Configure Drawing`, `Audit EG Surface`, `Export QA Package`,
`Package Deliverables`. `New Proposal` ships before the next full workflow
is developed, and each later workflow needs its own design.

`Promote Project` later completes the proposal-to-active-project lifecycle.
The awarded Autodesk Docs/Forma project and its destination folders are
created by an administrator; this repository never provisions cloud
projects.

Read-only inspection and QA (alignment/profile inventory and audit, corridor
health, grading and cut/fill QA, drainage-network audit, design-revision
comparison, redline tracking, then controlled feature-line creation) belong
to Phase 5. Parameter-driven design starters (building-pad preview, ditch
and berm, standard assemblies, road, plant-site, preliminary pipe network,
plan production) belong to Phase 6 and begin preview-only. A thin real
alignment/profile inventory may serve as a Phase 5 walking skeleton while
Phase 4 is underway if it stays small and does not delay `New Proposal`.

Guardrails on that sequence: the large majority of near-term effort goes to
the production foundation; internal tooling is never a milestone; every
enabling task names the user-facing workflow it unblocks; every milestone
ends with a live Civil 3D run and demonstrated time savings; empty commands,
placeholder classes, and schema-only slices are not walking skeletons;
coordination or governance machinery is added only when an active workflow
needs it; abstractions are added only when a real slice needs them.

### Release 1 boundary

Release 1 delivers one Civil 3D command, `New Proposal`, running inside
Civil 3D. No standalone launcher.

Required inputs: client name, site name, proposal year, sheet orientation,
sheet size. Optional inputs: client number, official project number,
proposal number or provisional identifier, site address, project manager.

The project root defaults to `Z_Proposal/<year>/Client Name - Site Name`.
The base path comes from configuration; no workstation-specific absolute
path is compiled in. Adding a client number later updates project
configuration, DST properties, and linked title-block fields only — it never
renames the proposal root.

The proposal workspace exists before an official project number does, on a
stable root and internal folder structure. Promotion into an
administrator-created project is `Promote Project`, not release 1.

### Drawings, references, and sheets

Three model drawings are created from the configured model template. Their
default filenames are owned by the standards manifest, not scattered through
code:

| Default filename | Role | Responsibility |
|---|---|---|
| `Base.dwg` | `BaseModel` | Raw-source aggregation; preserve source fidelity, minimize editing |
| `Existing Conditions.dwg` | `ExistingConditionsModel` | Curated existing-site linework and the approved existing-ground surface |
| `C-SP Linework.dwg` | `ProposedDesignModel` | Proposed design and linework |

Raw survey and supplied source files are preserved unchanged in the
configured database or support location. Recording provenance and
referencing them into `Base.dwg` is `Intake Source Files`; `New Proposal`
does not copy or clean source geometry.

No empty existing-ground surface is created to make the scaffold look
finished. The data-shortcut directory is created and the EG surface is
recorded as pending until a real surface exists.

Every reference is a relative-path overlay at insertion point `(0,0,0)`,
scale `1`, rotation `0`, unless company standards require otherwise.
`C-SP Linework.dwg` references `Base.dwg` and `Existing Conditions.dwg`.
Each sheet drawing references `Base.dwg`, `Existing Conditions.dwg`, and
`C-SP Linework.dwg` separately. Nested references are not relied on: the
overlays into C-SP are design context, and the direct sheet references give
independent visibility and layer control without duplication. The expected
reference graph lives in the project plan and configuration and is verified
after execution.

A sheet set (DST) is created during proposal setup and carried through the
proposal lifecycle. Three starter sheet drawings are registered: `Site
Overview / Site Location`, `Existing Conditions`, and `Proposed Site Plan`.
One standard starter profile is used; the user selects only orientation and
a supported sheet size, and those choices resolve the configured sheet DWT,
title block, page setup, printable area, and placeholder layout. Sheet
numbers and sheet drawing filenames are not invented — they enter the
standards manifest once the owner supplies the real conventions.

Labeled placeholders required: two on `Site Overview / Site Location`,
labeled `SITE LOCATION` and `SITE OVERVIEW`; one labeled viewport
placeholder on each of `Existing Conditions` and `Proposed Site Plan`.
Whether a placeholder is a real disabled paper-space viewport or only a
labeled boundary is an open owner input and is resolved before the
sheet-generation code is written, not guessed.

The run also creates the Civil-local project configuration, source register,
assumptions log, decision log, creation plan, and creation receipt. The
Civil-local configuration owns the facts needed to create and verify Civil
3D artifacts; it does not duplicate AutoGIS-owned analytical or GIS state.

### Architecture

The smallest boundary that keeps deterministic planning testable without
Autodesk installed, per
[ADR-0006](../../adr/0006-civil-production-accelerator.md).

The Autodesk-free core owns proposal inputs, standards-manifest parsing and
validation, naming and path safety, folder and artifact roles, the
deterministic planner, preflight rules that need no Autodesk, the proposed
action plan, verification contracts, and run-receipt contracts. The planner
converts inputs plus an approved manifest into a complete ordered previewable
plan and touches neither the filesystem nor an Autodesk API.

The native adapter owns command registration, the wizard, active-document
and host-version checks, native preflight, folder and file execution, DWG
creation from DWT files, sheet-set creation and property updates, sheet
registration, Xref creation, data-shortcut operations proven feasible
through supported APIs, native verification, and receipt presentation. The
command and UI layer stays thin: collect input, render the plan, obtain
explicit approval, invoke execution, display results.

Patterns are reused rather than reinvented: the staged fail-closed
orchestration and verified-result shape of `BundleValidator`; the immutable
`ValidationReport`/`ValidationIssue` result models; the
`CliApplication`/`TextReportRenderer` separation of entry point,
orchestration, and presentation; `ReferenceBoundaryTests` to hold the core
free of Autodesk references; the diagnostic kit's proven command
registration, active-document guard, and API access patterns; and the
existing central package management, lock files, warnings-as-errors,
deterministic builds, Windows CI, and golden fixtures.

The conceptual minimum of domain types is `ProposalInputs`,
`StandardsManifest`, `ProposalPlan`, `PlannedAction`, `ProposalPlanner`,
`PreflightReport`, `VerificationReport`, and `RunReceipt`, named to match
the conventions already in the code. No factory, service locator, plugin
framework, event bus, or per-class interface; an interface appears only at a
real test or platform boundary.

The handoff validator is left intact and is not this workflow's application
seam.

### Execution lifecycle

```text
Inputs
  → Validate
  → Build deterministic plan
  → Preview all actions
  → Explicit user approval
  → Execute in staging
  → Close created DWGs and DST
  → Verify outputs and references
  → Promote staging root to final proposal root
  → Write and display receipt
```

Preflight, before anything is created, verifies that the final target root
does not exist; that staging and target locations are writable; that the
client/site-derived name is valid and safe; that required templates exist
and are readable; that the orientation-and-size combination is supported;
that required page setups and title-block definitions exist; that Civil 3D
can create and save drawings; that required sheet-set operations are
available; and that any claimed data-shortcut operation is supported and
testable. Unsupported or ambiguous input fails closed with a stable issue
code and an actionable message, following the existing issue-code policy.

The proposal is built in a uniquely named staging directory beside the final
root, containing only files the run generated. Staging is not renamed to the
final root until every expected artifact exists, DWGs and the DST are
closed, sheet registration is correct, project metadata is correct, expected
Xrefs exist with valid relative paths, and the output matches the approved
plan.

On failure the final proposal root is not created; only the uniquely
identified staging directory the run created is removed; a failure receipt
is retained outside the proposal root; the exact failed operation and any
cleanup failure are reported; and a corrected rerun is permitted. Release 1
never merges into an existing proposal, repairs an incomplete one, or
overwrites existing files.

### Standards manifest

One versioned company-standards manifest replaces company detail in code. It
defines proposal base-root mapping by year or environment, folder structure,
default drawing filenames and roles, the model DWT path, sheet DWT paths by
orientation and size, supported size/orientation combinations, the DST
filename, sheet drawing filenames and sheet numbers, sheet titles,
title-block property mappings, page setups and plot settings,
viewport-placeholder geometry and labels, Xref intent and reference roles,
and the data-shortcut folder convention.

Proprietary company templates are never bundled in this repository.
Synthetic templates and manifests are used wherever licensing or repository
policy requires them.

## Implementation boundary

This design reserves these path prefixes in the roadmap marker, per the
phase-gate lifecycle, in addition to those the
[adapter-foundation design](2026-09-04-phase-4-adapter-foundation-design.md)
already reserved:

- `src/AutoGIS.Civil3D.Proposal/` — the Autodesk-free core.
- `tests/AutoGIS.Civil3D.Proposal.Tests/` — its test project.

Implementation proceeds in vertical slices, each either delivering a thin
usable path or directly unblocking the next: the core planner slice
(manifest model, input validation, naming and path rules, deterministic
plan, core tests and the Autodesk reference-boundary test); the native
command and preview slice (adapter project, command registration, thin
wizard, rendered preview, explicit approval or cancel, no drawing writes
before acceptance); the drawing and sheet-set execution slice (staging root,
folders, model DWGs, sheet DWGs, DST and registration, metadata, Xref
graph); the verification and receipt slice (native verification,
success/failure receipts, safe cleanup, existing-target refusal, post-run
summary); and the live qualification and packaging slice (real templates,
supported-release qualification, sanitized evidence, package update,
measured timing). Work is not split by architectural layer in a way that
leaves core infrastructure with no runnable workflow.

Focused disposable spikes resolve these before native implementation
details are committed, and their results are recorded as decisions rather
than kept as production code: sheet-set creation and editing through the
supported API in the target release; creation and registration of new sheet
DWGs from DWT layouts; title-block fields driven by DST custom properties;
relative overlay Xrefs across staged drawings; data-shortcut folder and
project association through supported APIs; and closing all created
documents and the DST before the staging rename.

## Acceptance evidence

Collected on a Phase 4 gate issue and cited by the eventual gate-change-log
row, following the Phase 0 and Phase 3 pattern. Passing CI is not
sufficient: the gate requires a qualification run in each supported Civil 3D
release using the owner's real approved templates, with sanitized evidence
for command loading, wizard and preview, drawing creation, DST creation and
sheet registration, title-block property updates, Xrefs and relative paths,
the data-shortcut behavior the release actually claims, verification and
receipts, and forced failure and cleanup.

The qualification demonstrates that a typical proposal is created in under
five minutes; that the configured folder structure exists; that the three
model drawings exist and open correctly; that three starter sheet drawings
exist; that the DST opens and lists all three sheets; that the selected
title block, orientation, size, and page setup are correct; that every
required viewport placeholder exists and is labeled correctly; that Base and
Existing Conditions are overlaid into C-SP and that all three are separately
overlaid into every sheet; that all Xrefs use valid relative paths; that the
data-shortcut directory exists with no meaningless EG surface; that project
metadata reaches the DST and linked title-block fields; that a client number
can be added later without renaming the proposal root; that re-running
against the same target blocks safely; that a forced mid-run failure leaves
no partial final proposal folder; that the success receipt matches the
files, sheets, and references actually created; and that the owner completes
a real proposal with the tool and confirms meaningful time savings.

Autodesk-free automated tests cover input validation for required and
optional fields, safe name and path derivation, `Client Name - Site Name`
root naming, missing-client-number behavior, later metadata update without
root rename, supported and unsupported orientation/size combinations,
deterministic plans from identical inputs, the complete planned folder and
artifact inventory, the planned Xref graph and overlay mode, planned starter
sheets and placeholder counts, existing-target conflict, missing-template
failure, stable issue codes, receipt content, and the core's reference
boundary. They use the concise table-driven style the existing xUnit suite
already supports, with no mocking framework.

Adapter integration tests cover filesystem staging, execution ordering,
verification, and failure cleanup at the narrowest practical boundary, with
a small fake used only where the Autodesk runtime cannot be present in
ordinary CI.

## Open owner inputs

Asked one at a time when the implementation reaches them, never fabricated,
and never blocking the documentation reset or Autodesk-free planning work.
A native-completion claim is blocked until they are answered:

1. The exact canonical folder tree beneath `Client Name - Site Name`.
2. The location of the approved model and sheet DWT files.
3. The supported orientation/size combinations.
4. The exact sheet drawing filenames and sheet numbers.
5. Which DST custom properties map to which title-block fields.
6. The page setup for each sheet size and orientation.
7. The exact viewport-placeholder dimensions and locations.
8. Whether a placeholder is a real disabled paper-space viewport or only a
   labeled boundary.
9. The Civil 3D releases the first production package must support.
10. The standard Sheet Set Manager mode in the organization.

## Exclusions

Not in release 1: a standalone proposal launcher; Autodesk Docs/Forma
project provisioning; proposal-to-active-project promotion; source-file
ingestion automation; parcel lookup; geocoding; regulatory research or
requirement extraction; basemap or imagery acquisition; automatic viewport
framing or scale selection; automatic EG surface creation; alignment,
profile, corridor, network, grading, or feature-line creation; automatic
plan production beyond the three starter sheets; AI-generated engineering
decisions; and silent repair, overwrite, or merge behavior.

No contract change: v1 is frozen. No change to the validator, the CLI, the
fixture corpus, the diagnostic kit, or its preserved evidence. No
parking-lot items.

## Known ceilings

- The standards manifest is specified but its content is unknown until the
  owner supplies it. Planning, validation, and tests proceed against
  synthetic manifests; the shape may need one revision when real values
  arrive.
- Data-shortcut operations are claimed only to the extent the spikes prove
  them through supported APIs. If a spike fails, the affected step becomes a
  recorded manual step in the receipt rather than a silent omission.
- Placeholder semantics are unresolved, and the sheet-generation code cannot
  be finished before that answer.
- Qualification evidence is per-release. A release without a qualification
  run is unsupported, whatever the build matrix contains.
