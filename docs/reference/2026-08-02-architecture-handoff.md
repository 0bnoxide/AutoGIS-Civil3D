# Preserved original: AutoGIS + Civil 3D architecture handoff

The owner's pre-repository architecture and brainstorming handoff that seeded
this project, preserved verbatim below (2026-08-02). It is a dated historical
record, not operating policy: where it differs from the living documents, the
[roadmap](../roadmap.md), [architecture map](../architecture.md), and
[agent guide](../agent-guide.md) win.

---

# AutoGIS + Civil 3D AI/Automation Architecture Handoff

**Date:** August 2, 2026  
**Owner:** Greg / `0bnoxide`  
**Existing repository:** `0bnoxide/AutoGIS`  
**Recommended companion repository:** `0bnoxide/AutoGIS-Civil3D`  
**Current Civil 3D version:** 2025, with a likely move to 2026

## 1. Why this project exists

Greg is increasingly being asked to perform civil engineering design work in
Autodesk Civil 3D, including road and plant/site work. His Civil 3D and civil
design knowledge is currently novice-to-intermediate. He already uses AI heavily
to learn Civil 3D and ArcGIS workflows and wants to increase both capability and
speed through deliberate automation.

The goal is not to build an AI that independently "designs a road." The goal is
to build a dependable system that helps Greg:

- understand the correct design workflow and the reasoning behind it;
- collect the required design inputs before work begins;
- inspect the actual Civil 3D drawing instead of relying on screenshots and
  incomplete descriptions;
- automate repetitive and deterministic work;
- compare designs against configurable standards and project criteria;
- preview proposed changes before anything is written to a drawing;
- preserve calculations, assumptions, warnings, and approvals for QA;
- move data cleanly between Civil 3D, ArcGIS Pro, AutoGIS, survey/UAS outputs,
  and reports;
- turn completed work into reusable company/project-specific design recipes.

AI should make Greg faster and more capable, but geometry, calculations, units,
object creation, and compliance checks must remain deterministic and testable.
The user and the responsible engineer retain design authority.

## 2. Core architectural decision

The recommended architecture is:

> **AutoGIS remains the Python/GIS/data/automation core. Civil 3D receives a
> separate, first-class .NET add-in that integrates with AutoGIS through a
> versioned bridge contract.**

The Civil 3D project should be a companion repository, tentatively named:

`0bnoxide/AutoGIS-Civil3D`

This is separate in source control and runtime, but part of the same product
system. "Separate" does not mean loosely connected. Deep integration should be
created through an intentional API, shared schemas, job identifiers, structured
results, and coordinated releases—not by mixing Python and C# code into one
runtime.

Starting with .NET from the beginning strengthens this recommendation. The
Civil 3D add-in should not begin as a collection of temporary scripts that later
has to be rewritten. It should start with a proper add-in structure, a stable
boundary around Autodesk APIs, and automated tests for everything that does not
require a live Civil 3D process.

## 3. Why the Civil 3D add-in should be separate

Civil 3D and AutoGIS have materially different constraints:

- Civil 3D add-ins run in-process inside AutoCAD/Civil 3D and must match the
  Autodesk host release and managed assemblies.
- AutoGIS uses Python, ArcGIS/ArcPy, command-line tools, GUI/toolbox surfaces,
  and potentially headless processing environments.
- A failed Python/GIS operation should not destabilize the Civil 3D process.
- Civil 3D 2025 and 2026 require release-specific Autodesk references and
  packaging.
- Company deployment, code signing, trusted paths, and CAD-manager approval are
  different from installing a normal Python package.
- ArcGIS Pro's managed Python environment should not be embedded into or loaded
  inside Civil 3D.

Keeping the repositories separate prevents the existing AutoGIS build,
dependency, and release process from becoming coupled to Autodesk DLLs and a
Windows-only Civil 3D installation. It also allows the Civil 3D add-in to be
versioned, packaged, signed, and deployed on its own schedule.

The existing AutoGIS architectural principle still applies: business logic
shared by the CLI, GUI, `.pyt` toolbox, notebooks, or Civil 3D integration
belongs in `autogis.core`, not in each interface. Notebooks remain a thin
analysis/exploration layer. Likewise, Civil 3D command handlers and palette UI
should remain thin; host-specific operations belong in testable .NET services,
while cross-product GIS/data logic remains in AutoGIS.

## 4. System responsibility split

### AutoGIS-Civil3D .NET add-in owns

- AutoCAD/Civil 3D command registration and lifecycle
- The Civil 3D ribbon, palette, prompts, selection, and drawing interaction
- Reading Civil 3D document, database, unit, coordinate-system, style, and
  object information
- Mapping Civil 3D object IDs/handles into stable request records
- Autodesk transactions, document locks, undo groups, and object creation
- Deterministic validation that requires Autodesk geometry or Civil 3D APIs
- Preview graphics and proposed-change summaries
- Applying only explicitly allowed and validated actions
- Reporting exact changes, warnings, and failures back to AutoGIS
- Release-specific adapters for Civil 3D 2025 and 2026

### AutoGIS owns

- GIS data ingestion, transformation, and export
- ArcGIS Pro/ArcPy workflows
- Survey, RTK, UAS, DEM, raster, point, and project-data processing
- Cross-product spatial analysis
- Standards and rule-set storage that is not specific to Autodesk objects
- AI orchestration, retrieval, prompting, and model-provider configuration
- Project/job records, artifact provenance, and reporting
- Long-running processing outside the Civil 3D process
- Existing shared business logic in `autogis.core`
- CLI, GUI, `.pyt`, and notebook consumers of that same core

### The bridge contract owns

- Request and response schemas
- Contract version
- Job and correlation identifiers
- Drawing fingerprint and host-version metadata
- Unit and coordinate-system declarations
- Civil object summaries and stable references
- Input/output artifact locations and hashes
- Allowlisted action types and parameters
- Preconditions, warnings, errors, and validation results
- Approval and execution state
- Provenance, standard references, and timestamps

## 5. Proposed system shape

```text
Civil 3D drawing
      |
      v
Civil 3D 2025/2026 host adapter
      |
      v
Shared .NET add-in core + palette + validation + transaction executor
      |
      v
Versioned AutoGIS bridge contract
      |
      +---- transparent file-job transport for the first pilot
      |
      +---- loopback HTTP transport for responsive production integration
      v
autogis.bridge
      |
      +---- autogis.core / GIS / ArcPy workflows
      +---- deterministic rules and QA checks
      +---- AI planning and explanation
      +---- standards/document retrieval
      +---- reports, artifacts, and job history
```

The .NET add-in is the trusted Civil 3D actuator. AutoGIS is the data and
analysis engine. AI is the planner, explainer, and reviewer. AI should never be
the direct actuator.

## 6. Bridge and transport recommendation

The contract should be transport-neutral from the beginning. Define the request
and result objects before committing to how they move between processes.

### Initial transport: explicit file jobs

For the first real integration slice, use a transparent job directory containing
JSON plus referenced artifacts such as GeoJSON, CSV, LandXML, or raster files.
This is easy to inspect, reproduce, archive, and explain to IT. It avoids
embedding Python and lets both sides be tested independently.

Example lifecycle:

1. The add-in creates `request.json` and any required exported artifacts.
2. AutoGIS processes the job through a CLI or broker command.
3. AutoGIS writes `result.json` and derived artifacts.
4. The add-in validates the response contract and presents the result.

### Production transport: loopback service

Once the file-based round trip is proven, add a local AutoGIS bridge service
bound only to `127.0.0.1`. Python/FastAPI and .NET `HttpClient` provide a simple,
well-tested path with OpenAPI-generated clients and schemas. Use a per-user
token, do not bind to the LAN, validate message size, and keep the file-job
transport available for troubleshooting and organizations that prohibit local
services.

Do not embed CPython, ArcPy, or the ArcGIS Pro Python environment directly into
Civil 3D. Do not make the primary integration depend on keystroke automation,
AutoCAD command strings, or an LLM emitting raw commands.

## 7. Suggested repository structure

```text
AutoGIS-Civil3D/
  src/
    AutoGIS.Civil3D.Contracts/
    AutoGIS.Civil3D.Core/
    AutoGIS.Civil3D.UI/
    AutoGIS.Civil3D.Host2025/
    AutoGIS.Civil3D.Host2026/
  tests/
    AutoGIS.Civil3D.Contracts.Tests/
    AutoGIS.Civil3D.Core.Tests/
    AutoGIS.Civil3D.IntegrationTests/
  bundle/
    AutoGIS.Civil3D.bundle/
      PackageContents.xml
      Contents/
        2025/
        2026/
  tools/
    diagnostics/
  docs/
    architecture.md
    bridge-contract.md
    security-and-deployment.md
    version-matrix.md
  scripts/
    build-2025.ps1
    build-2026.ps1
    install-current-user.ps1
```

On the AutoGIS side, add a deliberately bounded integration surface such as:

```text
autogis/
  bridge/
    contracts/
    handlers/
    service/
    artifacts/
  core/
```

The bridge may call `autogis.core`; it must not create a second implementation
of AutoGIS business logic. The .NET UI and command classes should similarly call
service classes instead of containing geometry or workflow logic themselves.

## 8. Civil 3D 2025 and 2026 strategy

Civil 3D 2025 moved managed plug-ins to .NET 8 and uses AutoCAD release series
`R25.0`. Civil 3D 2026 remains on .NET 8 but uses `R25.1` and newer Civil 3D
assemblies. The practical hurdle is not a full rewrite; it is maintaining and
testing release-specific Autodesk references and bundles.

Recommended strategy:

- Keep most code in shared projects with no direct dependency on a specific
  Autodesk release where practical.
- Compile a 2025 host assembly against the 2025 AutoCAD/Civil 3D DLLs.
- Compile a separate 2026 host assembly against the 2026 DLLs.
- Give each assembly its own bundle subdirectory and manifest entry.
- Keep Autodesk assemblies `Copy Local=false`; do not redistribute them.
- Run integration tests on machines that actually have the corresponding Civil
  3D release installed.
- Do not relabel a 2025 build as 2026 or assume Civil 3D API binary compatibility
  merely because both hosts use .NET 8.

There is no compelling development reason to switch to Civil 3D 2026 early.
Build and prove the 2025 diagnostic and read-only vertical slice on the software
currently installed. Design the adapter boundary now so adding 2026 is routine.
If the work environment's switch is imminent, avoid investing heavily in
2025-only write operations until the 2026 adapter can be exercised.

Official Autodesk references:

- <https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm>
- <https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-A31588E9-2A5F-4BF1-878D-DBE2564E2A99.htm>
- <https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-267E68C8-AD2D-4F7F-87DF-831018D56CDB.htm>
- <https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-6FDC9D3D-FAB2-453E-A7BF-F1CC82F4AE18.htm>

## 9. Correct role for AI

AI should be used for work that benefits from language, context, and synthesis:

- translating a design objective into a checklist and required inputs;
- explaining Civil 3D concepts and the reason for each step;
- retrieving applicable company, municipal, DOT, client, or project standards;
- identifying missing assumptions and contradictory settings;
- proposing a structured action plan;
- explaining Civil 3D errors and likely fixes;
- comparing deterministic QA results against documented criteria;
- drafting calculation narratives, design notes, and reports;
- turning completed workflows into reusable playbooks.

AI should not be trusted to directly perform:

- coordinate transformations without explicit source/target systems;
- survey-foot/international-foot or datum decisions by inference;
- final geometric calculations;
- final alignment, profile, grading, drainage, or corridor design decisions;
- unreviewed Autodesk transactions;
- arbitrary command execution or code execution from model output;
- claims of regulatory or design-standard compliance without cited criteria.

Use a structured intent format. The model can propose an allowlisted action such
as `analyze_surface`, `audit_alignment`, or `create_preview_feature_line`; the
.NET and Python code validates the schema, preconditions, units, limits, and
permissions before any operation occurs.

## 10. Recommended user workflow

Every AI-assisted design workflow should follow the same state machine:

1. **Read** — Collect the active drawing, units, coordinate system, host
   version, selected objects, styles, dependencies, and relevant project data.
2. **Ask** — Identify missing design criteria and assumptions rather than
   silently inventing them.
3. **Plan** — Produce a structured sequence with source standards and expected
   outputs.
4. **Preview** — Show affected objects, calculations, warnings, and proposed
   changes without modifying the drawing.
5. **Approve** — Require explicit user approval for the bounded operation.
6. **Execute** — Apply deterministic .NET operations inside a document lock,
   transaction, and undo group.
7. **Validate** — Re-read the resulting objects and run deterministic checks.
8. **Report** — Save results, assumptions, standard references, warnings, object
   identifiers, and artifact hashes.

This pattern is more important than building a chatbot inside Civil 3D. The
first UI should be a focused palette with context, findings, proposed actions,
and approval—not a general-purpose chat window.

## 11. AI learning and production modes

The system should eventually expose two deliberate modes:

### Coach mode

- Explains each Civil 3D step and why it is needed
- Shows what object or setting the step affects
- Links the recommendation to a standard or source
- Warns about common failure modes
- Lets Greg perform the steps manually
- Records the actual outcome as a reusable playbook

### Production mode

- Uses a previously reviewed recipe
- Reads the actual drawing context
- Pre-fills known project parameters
- Runs deterministic automation
- Stops on missing inputs or failed preconditions
- Produces a preview/diff and requires approval
- Validates and records the result

Coach mode is how knowledge is built. Production mode is how proven knowledge
becomes speed.

## 12. High-value automation opportunities

### Cross-cutting setup and QA

- Drawing version, units, coordinate-system, and survey-foot audit
- Template, layer, style, label-set, and naming checks
- Xref/data-reference path audit
- Broken or stale data shortcut detection
- Object inventory and drawing health report
- Project folder and deliverable scaffolding
- Automated calculation/design-assumption log
- GIS/CAD import and export provenance

### Surfaces and survey/UAS data

- DEM/point/point-cloud intake checks
- Unit, datum, and coordinate-system verification
- Surface boundary and breakline inventory
- Spike, hole, flat-triangle, and suspicious-elevation detection
- Surface comparison and volume setup
- Cut/fill summaries with input hashes and surface versions
- GIS constraint overlays and data-age warnings

### Roads and corridors

- Required-input checklist: road class, design speed, design vehicle,
  jurisdiction, curve/grade criteria, drainage assumptions, and tie-ins
- Alignment curve and tangent audit
- Profile grade and vertical-curve audit
- Station-range and sample-line checks
- Corridor region gap/overlap detection
- Missing target and outdated-corridor detection
- Assembly/code-set consistency checks
- Corridor-surface boundary and daylight review
- Quantity and exception reporting

### Plant and general site design

- Existing-ground and constraint summary
- Candidate pad elevations and deterministic cut/fill comparison
- Access-road slope and tie-in checks
- Surface low-point and likely ponding detection
- Drainage-path and grading-slope review
- Feature-line elevation/slope consistency
- Daylight convergence and steep-slope flags
- Utility, parcel, wetland, structure, or setback conflict overlays
- Construction-sheet notes, quantities, and design-assumption summaries

These tools should initially diagnose and explain. Write automation should be
added only after the read-only analysis is trusted.

## 13. Automation maturity ladder

Do not jump directly from manual drafting to autonomous drawing changes.

1. **Guidance:** AI-generated checklists, explanations, and reusable prompts.
2. **Templates:** standardized folders, drawing templates, styles, scripts, and
   report structures.
3. **Read-only inspection:** programmatic drawing context and QA reports.
4. **Round-trip analysis:** Civil 3D exports context; AutoGIS returns structured
   findings linked to Civil objects.
5. **Preview actions:** the system builds proposed geometry or settings in a
   noncommittal preview.
6. **Approved writes:** bounded, deterministic changes with undo and validation.
7. **Batch recipes:** proven workflows applied to standardized inputs with
   exception handling and human review.

The largest near-term productivity gain will probably come from levels 2–4,
not from attempting autonomous design.

## 14. First recommended vertical slice

After the diagnostic plug-in loads, build `AUTOGISSCAN`.

### Civil 3D side

Collect and export:

- Civil 3D/AutoCAD/API versions
- Drawing path plus a privacy-safe drawing fingerprint
- Drawing units, insertion units, foot conversion, coordinate-system code, and
  scale
- Surface, alignment, profile, site, corridor, pipe-network, and COGO point
  inventories
- Selected object metadata where applicable
- Data-reference/rebuild status where exposed safely
- Warnings encountered while reading the drawing

### AutoGIS side

- Validate the request contract
- Analyze units and coordinate-system consistency
- Identify missing or suspicious project metadata
- Produce a structured QA response
- Return human-readable findings, severity, evidence, and recommended next
  action

### Civil 3D result

Display findings in a palette. Where possible, findings should retain Civil
object references so Greg can select or zoom to the affected object. This proves
deep integration without risking drawing changes.

The first approved write operation should be small and reversible, such as
creating proposed geometry on a dedicated AutoGIS preview layer or converting a
validated GIS feature into preview Civil geometry. It should not be corridor or
grading automation.

## 15. Contract requirements

At minimum, every bridge request should carry:

- `contract_version`
- `job_id`
- `correlation_id`
- `operation`
- Civil 3D product/version/build
- add-in version
- drawing fingerprint
- coordinate-system code
- drawing units and foot definition
- selected object references and summaries
- explicit parameters
- input artifact paths and SHA-256 hashes
- requested output format
- timestamp and initiating user/machine identifier at an appropriate privacy
  level

Every result should carry:

- matching contract/job/correlation identifiers
- success, partial, or failure status
- findings with severity and evidence
- warnings and exceptions
- produced artifact references and hashes
- proposed actions in an allowlisted schema
- standards/source references
- elapsed time and component versions
- whether any drawing changes are requested

Reject unknown contract versions and unknown action types. Never execute a
natural-language instruction directly.

## 16. Security and work-PC deployment

The normal no-admin deployment path is:

`%APPDATA%\Autodesk\ApplicationPlugins`

An initial DLL can also be tested with `NETLOAD`. Neither operation normally
requires administrator privileges. The real constraints are likely to be:

- installation of the .NET 8 SDK for compiling;
- `SECURELOAD`, `TRUSTEDPATHS`, and `APPAUTOLOAD`;
- AppLocker, WDAC, Defender, or other endpoint controls;
- unsigned internal binaries;
- company restrictions on cloud AI or project-data transmission;
- restrictions on local services or child processes.

Do not work around company security settings. Prove the smallest read-only DLL,
document any block, and request an approved path from CAD/IT. For broader
deployment, use an organization-controlled build, Authenticode signing,
documented hashes, versioned bundles, and an IT-managed installation process.

AI and bridge features must have explicit data-handling modes. Sensitive drawing
names, coordinates, client data, or geometry must not be sent to an external
model unless company policy permits it. Deterministic local automation should
remain usable even when AI is disabled.

## 17. Current implementation status

A Civil 3D 2025 diagnostic source/build kit has been created:

`AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip`

It contains:

- a .NET 8/x64 Civil 3D 2025 project;
- the read-only `AUTOGISDIAGNOSTICS` command;
- a Civil 3D-only `R25.0` bundle manifest;
- build-time discovery and version validation of Autodesk assemblies;
- current-user install/uninstall scripts;
- static validation tests and IT pilot notes.

The kit is intentionally not precompiled. It must be compiled on the Windows
workstation against the installed Civil 3D 2025 assemblies. Its immediate
purpose is to identify whether custom .NET development is allowed and what IT
or security barriers exist before building the production bridge.

## 18. Phased implementation roadmap

### Phase 0 — Foundation and security pilot

- Compile and run `AUTOGISDIAGNOSTICS` on Civil 3D 2025.
- Record build, `NETLOAD`, and command output.
- Determine whether the .NET 8 SDK requires IT installation.
- Determine signing/trusted-path/application-control requirements.
- Create `0bnoxide/AutoGIS-Civil3D` and import the diagnostic under
  `tools/diagnostics`.

**Exit condition:** A read-only managed command reliably loads and executes on
the work computer.

### Phase 1 — Shared solution and contracts

- Create shared contracts, core, UI, Host2025, and test projects.
- Define bridge contract v0.1 in JSON Schema or OpenAPI.
- Add sample request/result fixtures.
- Add structured logging and correlation IDs.
- Create a Civil 3D version/build matrix.

**Exit condition:** Both .NET and Python validate the same fixtures and reject
invalid messages.

### Phase 2 — Read-only `AUTOGISSCAN`

- Implement drawing-context collection.
- Export a transparent file job.
- Add an AutoGIS bridge handler and QA response.
- Display linked findings in a Civil 3D palette.

**Exit condition:** A real drawing can make a complete Civil 3D → AutoGIS →
Civil 3D round trip without drawing changes.

### Phase 3 — Standards-aware coach

- Index company CAD standards, project criteria, approved municipal/DOT
  references, and vetted internal workflows.
- Require source references for recommendations.
- Add missing-input detection and task-specific checklists.
- Record accepted workflows as recipes that call deterministic functions.

**Exit condition:** The assistant produces drawing-aware, sourced guidance that
Greg can verify and follow manually.

### Phase 4 — Previewable actions

- Define a small allowlist of action schemas.
- Add preview layers or transient graphics.
- Add precondition validation, approval, undo groups, post-execution validation,
  and change reports.
- Start with one small reversible geometry workflow.

**Exit condition:** One proposed change can be previewed, approved, executed,
undone, and independently validated.

### Phase 5 — Domain workflow modules

- Surface/UAS intake and QA
- GIS constraint overlay
- Alignment/profile audit
- Corridor health and target audit
- Site grading and ponding review
- Quantities and deliverable automation

Each module should progress through inspect → explain → preview → write rather
than beginning with write automation.

### Phase 6 — Civil 3D 2026 and enterprise deployment

- Add and test the Host2026 assembly and `R25.1` bundle entry.
- Set up an approved Windows/Civil 3D build machine.
- Add Authenticode signing and release packaging.
- Establish update, rollback, version-support, and sanitized-DWG testing
  procedures.

## 19. Decisions to preserve

- Keep AutoGIS as the shared Python/GIS core.
- Build Civil 3D integration as a separate first-class .NET companion project.
- Integrate through versioned contracts, not embedded Python or duplicated
  business logic.
- Make AI the planner/explainer/reviewer, not the geometry engine or direct
  drawing actuator.
- Begin with read-only inspection and a complete round trip.
- Require explicit previews and approvals before writes.
- Keep all write operations deterministic, allowlisted, transactional,
  undoable, validated, and logged.
- Maintain separate Civil 3D 2025 and 2026 host builds.
- Use per-user deployment for pilots; involve CAD/IT rather than weakening
  security.
- Preserve a non-AI/local path for deterministic automation.
- Keep notebooks, UI handlers, and adapters thin over shared core services.

## 20. Approaches explicitly not recommended

- Putting all C# source and Autodesk build dependencies directly into the
  existing AutoGIS Python package/release pipeline
- Embedding ArcGIS Pro Python or ArcPy inside Civil 3D
- Treating AutoLISP, Dynamo, keyboard macros, or generated command strings as
  the main integration architecture
- Allowing an LLM to emit and execute arbitrary Civil 3D commands
- Building a general chatbot before drawing-context collection and deterministic
  tools exist
- Starting with automated corridor or grading creation
- Assuming one DLL will safely cover both Civil 3D 2025 and 2026
- Storing business rules separately in the CLI, GUI, notebooks, and plug-in
- Sending client geometry or project data to cloud AI without explicit policy
  approval
- Weakening trusted-path or endpoint-security settings to make a pilot work

## 21. Immediate next actions for the receiving agent

1. Do not redesign the architecture before testing the diagnostic.
2. Help Greg compile the diagnostic kit on the Civil 3D 2025 work computer.
3. Diagnose any SDK, Autodesk-reference, `NETLOAD`, signing, or application-
   control failure from the exact output.
4. Once it passes, scaffold `0bnoxide/AutoGIS-Civil3D` using the shared-core and
   host-adapter structure above.
5. Draft bridge contract v0.1 and sample fixtures before implementing transport.
6. Implement `AUTOGISSCAN` as the first production vertical slice.
7. Coordinate the corresponding `autogis.bridge` boundary in the existing
   AutoGIS repository without moving or duplicating `autogis.core` logic.
8. Keep every early command read-only until the full round trip is reliable.

## 22. Open questions to resolve during implementation

- Which work tasks consume the most time today: project setup, surfaces,
  grading, alignments/profiles, corridors, quantities, sheets, or GIS/CAD
  exchange?
- Which company, client, municipal, or DOT standards may be indexed and used by
  AI?
- Is external/cloud AI permitted for project information, or must the system
  sanitize or remain local?
- Will IT install the .NET 8 SDK or provide an approved build machine?
- Does the organization have an internal code-signing certificate?
- Are loopback services permitted, or should file jobs remain the deployment
  transport?
- When is the actual Civil 3D 2026 migration expected?
- Which sanitized drawings can become repeatable integration-test fixtures?
- What should be the first small, reversible write operation after
  `AUTOGISSCAN`?

The strongest recommended first product is not an autonomous designer. It is a
drawing-aware Civil 3D copilot that can inspect the real model, retrieve the
right standards, explain the workflow, run deterministic checks, and turn a
reviewed plan into transparent, reversible automation.
