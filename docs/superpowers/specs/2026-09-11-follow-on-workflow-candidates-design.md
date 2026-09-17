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
- **Prior art** (pulled 2026-09-15 for the staging question; links only,
  no code adopted):
  - Side-database editing is the practitioner consensus for changing a
    drawing not open in the editor: `Database(false, true)`, `ReadDwgFile`,
    `CloseInput(true)` before `SaveAs` to the same name, working-database
    redirection kept short, and `StartOpenCloseTransaction` because a
    no-document database has no document locking or undo.
    [Autodesk developer blog, 2012](https://blog.autodesk.io/using-readdwgfile-with-net-attachxref-or-objectarx-acdbattachxref/).
    Implies: the staging model's attach step runs on a side database, and
    the transaction type is a design rule, not a detail.
  - `AttachXref` succeeding does not mean the reference resolves; on a side
    database call `ResolveXrefs` and read `GetHostDwgXrefGraph` or each
    record's `XrefStatus`.
    [Autodesk .NET forum, 2017](https://forums.autodesk.com/t5/net-forum/loading-an-external-database-and-not-being-able-to-read-the/td-p/6786923).
    Implies: the Verify rule "every planned attachment resolves through a
    relative path" is checked by resolution status, not by a non-null id.
  - `eFileAccessErr` from `AttachXref` has two reported causes: the call
    outside a document lock, and an xref name equal to the host drawing's
    name.
    [Autodesk .NET forum, 2021–2023](https://forums.autodesk.com/t5/net-forum/attachxref-has-an-efileaccesserr-error/td-p/10732177).
    Implies: a preflight that the derived reference name differs from
    `Base`, and an owner input on how source filenames map to reference
    names.
  - Relative-path conversion needs the host saved to a real path first;
    `AttachXref`/`OverlayXref` are the only supported routes — a hand-built
    block reference is not an xref.
    [Kean Walmsley, 2015](https://keanw.com/2015/11/creating-autocad-xrefs-as-overlays-with-relative-paths-using-net.html);
    [Autodesk .NET forum, 2015](https://forums.autodesk.com/t5/net-forum/c-xref-attach-set-type-to-attach-and-set-path-to-relative-on/td-p/5554360).
    Implies: Verify reads the record's `XrefType` and stored path after
    attach, and a fixture exercises attach and overlay separately.
  - `XrefFileLock.LockFile` throws when the target is open elsewhere or
    read-only, and a failed lock can fault again on finalization.
    [Kean Walmsley, 2015](https://keanw.com/2015/01/modifying-the-contents-of-an-autocad-xref-using-net.html).
    Implies: the swap step prechecks open/read-only state before touching
    the prior `Base.dwg`, and the failure path is designed for a double
    fault.
  - Under Desktop Connector, paths over 260 characters or containing
    `<>:"/|?*` break references; whether relative paths survive at all is
    contested in Autodesk's own forum (an Autodesk collaborator says
    references always become absolute local-cache paths; a user reports
    relative paths surviving when the host was uploaded through the web
    UI).
    [Autodesk support KB](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/XREF-files-are-not-found-or-broken-links-appear-when-opening-the-drawing-through-Desktop-Connector-in-Civil-3D.html);
    [BIM 360 support forum, 2019](https://forums.autodesk.com/t5/bim-360-support-forum/desktop-connector-how-to-manage-xrefs/td-p/9217478).
    Implies: a path-length preflight and a forbidden-character preflight on
    each file or directory name, not on drive prefixes or path separators;
    and a sixth open owner input — whether a proposal root is ever under
    Docs/Desktop Connector, since relative-path attachment is unproven there.
  - Gap: no source describes a copy → attach → verify → swap-with-prior-
    retained model as a named pattern. The staging model is a
    project-specific decision, not something to copy.

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
- **Prior art** (pulled 2026-09-15 for the plotting route; most sources
  are AutoCAD-general and several predate 2024, so behaviour on 2026 is
  unverified until qualified):
  - Plotting needs the graphics pipeline, which a side database does not
    have: "AutoCAD currently needs the graphics pipeline to generate
    printed graphics, and this is not present for side databases."
    [Kean Walmsley, 2007](https://keanw.com/2007/07/accessing-dwg-f.html).
    Implies: the reference-closure walk may use side databases; the plot
    step must open each sheet drawing as a resident document. That is a
    design constraint, not an implementation detail.
  - The two established routes both yield one multi-page file per batch:
    `PlotEngine` with one `BeginDocument` and a `BeginPage` per layout,
    and DSD-driven `Publisher.PublishDsd`/`PublishExecute`. Each layout
    must be made current before `PlotEngine` plots it; mixed paper sizes
    across layouts throw `eInvalidPlotInfo`; device and canonical media
    names must match the plotter's lists exactly or `eNotValidInput`
    follows.
    [Kean Walmsley, 2007](https://keanw.com/2007/09/driving-a-multi.html);
    [Kean Walmsley, 2007](https://keanw.com/2007/10/previewing-an-1.html);
    [Autodesk developer blog, 2012](https://blog.autodesk.io/how-to-use-the-autodeskautocadpublishingpublisherpublishdsd-api-in-net/);
    [APS blog, 2020](https://aps.autodesk.com/blog/publish-multiple-drawings-single-pdf).
    Implies: the Verify rule "PDF count equals selected sheet count"
    requires one plot document per sheet, so the plan is one plot action
    per sheet by construction; a preflight validates device and media
    names against `PlotSettingsValidator` lists; a fixture mixes paper
    sizes. A seventh open owner input: one PDF per sheet or one multi-page
    PDF per deliverable — the count rule changes with the answer.
  - A named page setup referenced across drawings was unreliable; using
    each drawing's own page setup as the source worked.
    [Autodesk developer blog, 2012](https://blog.autodesk.io/how-to-use-the-autodeskautocadpublishingpublisherpublishdsd-api-in-net/).
    Implies: preflight that the manifest's page setup exists in every
    selected sheet drawing, and a preflight failure if any is missing —
    consistent with the "missing layout fails preflight" rule above.
  - Sheet Set Manager access from .NET is versioned COM
    (`ACSMCOMPONENTS<NN>Lib`); manually re-registering its DLLs corrupts
    the install ("Class not registered"), reported March 2025 on 2024/2025.
    [Autodesk .NET forum, 2025](https://forums.autodesk.com/t5/net-forum/sheet-set-manager-api-basics/td-p/13350311).
    Implies: DST reading is a per-release binding and a qualification item
    of its own; no install step may register COM components.

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
- **Prior art** for candidates 4 and 5 (pulled 2026-09-15; Dynamo forum
  and package repositories; Autodesk forums were not reachable in this
  pass, so description-key, coordinate-system, and data-shortcut checks
  have no practitioner evidence yet):
  - `TinSurface.Triangles` fails on surfaces Civil 3D reports as valid
    when the outer boundary passes through particular vertices; reported
    on 2025 and 2026, recurring from October 2024 to March 2026, and fixed
    only by moving the vertex.
    [Dynamo forum](https://forum.dynamobim.com/t/tinsurface-triangles/105229).
    Implies: the "no outer boundary" check is too narrow — the risky state
    is a boundary Civil 3D accepts but triangle enumeration cannot walk.
    Spike detection depends on that walk, so a synthetic fixture with such
    a boundary is required before the spike check is trusted.
  - Practitioners force `Rebuild` rather than test `IsOutOfDate`; no
    thread branches on it.
    [Dynamo forum, 2024](https://forum.dynamobim.com/t/rebuild-surface-in-civil-3d-using-python/105951).
    Implies: keep the out-of-date check but prove it with our own fixture
    (edit a definition item, do not rebuild, assert out of date); field
    evidence for the member's reliability does not exist.
  - Point-group reads can return empty for a recognised, populated group
    until the session is restarted; surface and point-group creation fails
    on re-run when the name already exists.
    [Dynamo forum, 2026](https://forum.dynamobim.com/t/cogopointgroup-cogopoint-does-not-work-in-civil3d-dynamo/115056);
    [Dynamo forum, 2026](https://forum.dynamobim.com/t/dynamo-throwing-error-like-the-document-already-has-a-surface-with-the-same-name.../114643).
    Implies: Survey Preflight reports "group recognised, zero points" as
    its own issue code rather than "no duplicates"; any later creation
    workflow checks name existence first.
  - Civil3DToolkit is unavailable on 2025+ (community report; repository
    dormant since 2020, Apache-2.0) and Camber has had no release since
    2023 (permissive licence, confirm before any reference use); both are
    still run in the field against 2025/2026.
    [Dynamo forum, 2025](https://forum.dynamobim.com/t/node-add-non-destructive-breaklines/108963);
    [Civil3dToolkit](https://github.com/paoloemilioserra/Civil3dToolkit);
    [Camber](https://github.com/mzjensen/Camber).
    Implies: neither is a dependency or a correctness reference; Camber's
    surface-boundary nodes are the more current API-usage reference.
  - Broken data-shortcut states practitioners repair by hand (renamed
    source object or drawing, relocated or deleted object) are surfaced
    only as a Prospector icon.
    [WisDOT Civil 3D knowledge base](https://c3dkb.dot.wi.gov/Content/c3d/data-mgt/dm-repair-data-ref.htm).
    Implies: the "created but not built" check has no practitioner
    evidence of a programmatic path; validate it against the
    `DataShortcuts` API before designing around it.
  - No evidence found for: crossing-breakline detection, spike/sliver
    detection, elevation outliers, extent checks, coordinate-system
    mismatch. These checks stand on the owner's stated need alone.

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
6. Whether a proposal root is ever hosted under Autodesk Docs / Desktop
   Connector, since relative-path attachment is unproven there (candidate
   1 prior art).
7. One PDF per sheet or one multi-page PDF per deliverable; the
   verification count rule follows the answer (candidate 2 prior art).

## Exclusions

Not proposed here: Promote Project; Export QA Package; any Phase 6
parameter-driven starter;
alignment, profile, corridor, network, or grading creation; automatic
viewport framing; cloud project provisioning; any workflow that repairs,
merges, or silently overwrites an existing proposal.
