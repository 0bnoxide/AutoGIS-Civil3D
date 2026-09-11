# Follow-on production workflows after `New Proposal` — Candidate designs

**Status:** Proposed 2026-09-11. Not an owner decision, and not an
approved design until the owner records it as one. This document sequences
and shapes five candidate workflows drawn from the production-foundation
sequence the owner approved in the
[Civil Production Accelerator design](2026-09-04-civil-production-accelerator-design.md)
and [ADR-0006](../../adr/0006-civil-production-accelerator.md). It
authorizes no phase, reserves no path, and changes no roadmap marker.

Where this document departs from the approved sequence, the departure is
itself a proposal for an owner decision, not a reading of one. The
departures are: Package Deliverables is moved ahead of Configure Drawing,
Survey Preflight, and Audit EG Surface; Promote Project and Export QA
Package are left out. The approved design places all five candidates in
the production-foundation sequence, not in its Phase 5 list. Whether the
two read-only candidates instead belong to Phase 5 is the open question
[issue #121](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/121)
records, and this document does not settle it. Each candidate needs its own
approved design, and any gate decision it needs is recorded in the
[roadmap gate-change log](../../roadmap.md) before implementation begins.

## Problem

`New Proposal` establishes the shape every later workflow should share:
typed inputs, a versioned standards manifest, an Autodesk-free deterministic
planner, a rendered preview, explicit approval, execution in a staging
location, native verification against the plan, and a receipt that records
what was actually done. The approved design lists the workflows that follow
but does not say which fit that shape as-is, which strain it, and which
break it. Choosing the next slice without that analysis risks either a
workflow that quietly relaxes a release 1 guardrail (no edits to existing
files, no silent repair) or a shared framework extracted before a second
concrete workflow exists to justify it.

## The shared shape

Every candidate is judged against the lifecycle the accelerator design
fixes:

```text
Inputs → Validate → Plan → Preview → Approve → Execute (staged) → Verify → Receipt
```

A candidate that needs every stage is a **creation** workflow. A candidate
that stops after `Plan` and reports is an **inspection** workflow: it still
validates inputs, still produces a deterministic result from the same
inputs, still emits stable issue codes and a receipt, but it never writes a
drawing and needs no staging or promotion. Both are legitimate; the
distinction decides which core types each reuses.

## Candidates

Ordered by fit to the shared shape and by dependency on artifacts `New
Proposal` already creates. `Promote Project` is deliberately absent: it
depends on an administrator-created Autodesk Docs/Forma project that this
repository never provisions, so it cannot be qualified end to end from the
repository alone. `Export QA Package` is absent because its inputs are the
outputs of the two inspection candidates, so it cannot be shaped before
their result format is decided.

### 1. Intake Source Files (creation)

Records and references raw survey and supplied source files into
`Base.dwg` without altering them. The accelerator design already assigns
this responsibility here and excludes it from `New Proposal`.

- **Inputs:** proposal root; one or more source file paths; a source role
  per file (survey, record drawing, GIS export, imagery, other). The
  standards manifest has no source-role list today; adding one is a
  manifest change this workflow's design must make, and the role set is
  open owner input 2.
- **Plan:** for each source, a copy into the configured support location
  with the original filename preserved and a SHA-256 recorded in the source
  register. What follows depends on the format. A DWG source gets one
  relative-path Xref attachment into `Base.dwg` using the same reference
  conventions (overlay, insertion point, scale, rotation) that
  `ProposalPlanner` applies to the model and sheet graph. The manifest
  validates that graph as a closed set, so a source-reference entry is a
  second manifest change for this design. A georeferenced raster gets one
  relative-path image attachment, which has its own insertion semantics
  and is a distinct planned action, not an Xref. Every other format (point
  files, shapefiles, PDFs, spreadsheets) is copied and registered only;
  nothing is attached, and the register row says so.
- **Verify:** hash of the copied file equals hash of the original; every
  planned attachment resolves through a relative path; the source register
  lists exactly the planned rows, including the register-only ones.
- **Never:** cleans, reprojects, explodes, or edits source geometry;
  converts a non-DWG format into drawing content. Whether point files,
  shapefiles, or imagery later become DWG content is a separate owner
  decision with its own design; until then those formats have no attach
  path and this workflow does not pretend they do.
- **Guardrail tension:** this workflow attaches into `Base.dwg`, an existing
  file. It is the first workflow that writes into a drawing created by an
  earlier run. The staging model must be defined for a single drawing (copy,
  attach, verify, swap with the prior version retained) before code is
  written, and that definition is the open owner input this design cannot
  answer.

### 2. Package Deliverables (creation)

Produces the outgoing deliverable set from the proposal's sheet set.

- **Inputs:** proposal root; the DST; a deliverable profile from the
  manifest (which sheets, plot device, PDF naming rule, whether DWGs travel
  with the PDFs).
- **Plan:** walk the DST for registered sheets and keep the subset the
  deliverable profile selects; compute the reference closure of each
  selected sheet drawing; one plot action per selected sheet using the
  manifest's page setup; one ZIP action containing the PDFs, optionally the
  drawings with their closure, and a checksum manifest.
- **Verify:** PDF count equals selected sheet count, and each PDF maps to
  one selected sheet; every file in the ZIP is in the plan and every
  planned file is in the ZIP; the ZIP hash is written to the receipt.
- **Reuse:** none of the handoff packaging code writes a ZIP. It opens and
  validates the fixed two-entry contract-v1 bundle, and contract v1 is
  frozen. This workflow needs its own deliverables writer. What it can reuse
  are the validation primitives that are format-neutral (bounded entry
  reads, size limits, the staged fail-closed result shape), not the bundle
  reader or its manifest.
- **Never:** edits any drawing; emails or uploads anything. A selected
  sheet whose layout is missing fails preflight, and nothing is plotted.
- **Why second:** it depends only on artifacts `New Proposal` already
  creates, so it can be qualified on a synthetic proposal, and it is the
  workflow with the most visible time savings after project setup.

### 3. Configure Drawing (creation, strained fit)

Brings an existing drawing's settings into conformance with the manifest.

- **Inputs:** target drawing path; the manifest's drawing settings block
  (units, coordinate system, object layer mapping, style set, annotation
  scale list).
- **Plan:** a diff between the drawing's current settings and the manifest,
  rendered as one action per setting that would change, with current and
  planned values side by side. A drawing already in conformance yields an
  empty plan and no write.
- **Verify:** re-read every changed setting after the write and compare it
  to the planned value.
- **Never:** touches model or paper space entities; renames or purges
  layers; changes a setting the manifest does not name.
- **Guardrail tension:** like candidate 1, this edits an existing file. It
  also has no natural "empty target" check, so the existing-target refusal
  in `New Proposal` does not translate. It should not be built before
  candidate 1 settles the single-drawing staging model.

### 4. Survey Preflight (inspection)

Reports whether a survey point set is fit to build an existing-ground
surface from, before anyone builds one.

- **Inputs:** a point file or the COGO points in a named drawing; the
  proposal's coordinate system; the manifest's description-key set.
- **Checks:** duplicate point numbers; description codes with no key
  match; elevations outside a configured band or more than a configured
  distance from their neighbours; points outside the site extent; a point
  file coordinate system that disagrees with the proposal's.
- **Output:** an issue list with stable codes and a receipt naming the
  inputs, the check set and version, and the counts. No writes.
- **Reuse:** `ProposalIssue` and the issue-code policy. There is no
  standalone receipt type to reuse; the receipt is a planned support record
  today, so an inspection receipt is a new small type. No planner actions
  beyond "read", so `PlannedAction` is not reused.
- **Gate:** read-only inspection. Which phase it sits in is the question
  issue #121 records; it needs that answer before any code.

### 5. Audit EG Surface (inspection)

Closes the gap `New Proposal` leaves open on purpose: the EG surface is
recorded as pending and nothing checks the real one when it arrives.

- **Inputs:** a surface name in a named drawing or a data shortcut; the
  manifest's surface audit thresholds.
- **Checks:** surface out of date against its definition; no outer
  boundary; crossing breaklines; triangles steeper than a threshold with
  an edge shorter than a threshold (spikes); definition items pointing at
  missing files; a data shortcut that is created but not built.
- **Output:** as candidate 4.
- **Gate:** as candidate 4. Candidates 4 and 5 share their result and
  receipt shape and should be designed together even if built one at a
  time.

## What not to build

No shared workflow framework is extracted before a second creation workflow
exists. `ProposalPlanner`, `ProposalPlan`, and `PlannedAction` are
proposal-specific by design, and the accelerator design's guardrail is that
an abstraction appears only when a real slice needs it. The sequence is:
build candidate 1 as a second concrete workflow with its own planner and
plan types, then extract only what the two actually share. Extracting first
produces the plugin framework the accelerator design rejects.

Nothing in this document becomes a milestone. Every candidate is a
user-facing workflow; the enabling work each needs (a single-drawing staging
model, a deliverables writer, an inspection result shape) is named against
the workflow it unblocks and is not scheduled on its own.

## Open owner inputs

Asked when a candidate's design is started, never assumed:

1. The single-drawing staging and rollback model for workflows that edit an
   existing drawing (candidates 1 and 3).
2. The source roles and the support location convention for intake.
3. The deliverable profile: which sheets ship, PDF naming, whether drawings
   travel with PDFs.
4. Whether inspection results are shown only in the command's output,
   written beside the proposal, or both.
5. Survey and surface thresholds, which belong in the standards manifest.

## Exclusions

Not proposed here: Promote Project; Export QA Package; any Phase 6
parameter-driven starter;
alignment, profile, corridor, network, or grading creation; automatic
viewport framing; cloud project provisioning; any workflow that repairs,
merges, or silently overwrites an existing proposal.
