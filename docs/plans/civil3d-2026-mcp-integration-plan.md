# Civil 3D 2026 MCP integration plan

**Status:** Proposed for owner review, 2026-09-24. This is an evaluation deliverable, not an approved design or implementation authorization. Baseline: [`3bdf45c`](https://github.com/0bnoxide/AutoGIS-Civil3D/tree/3bdf45c5d8d437f9be2a129803f9a9f34393ea43). The [candidate evaluation](../research/civil3d-2026-mcp-evaluation.md) supplies the source evidence.

## Decision and first milestone

Keep the working Civil 3D installation free of third-party MCP bridges. **M1 is a new product capability and has no implementation authorization yet.** The owner must decide whether this facade belongs within the currently authorized Phase 4 scope. If not, defer it until its owning phase is authorized or approve a roadmap amendment. Record the decision in the gate-change log; a change to phase sequence or architecture also needs an ADR. Phase 2 acceptance and Phase 4 authorization do not implicitly cover this tool. After that gate and a specific MCP design are approved, the first useful milestone should be **one local, read-only `validate_handoff_bundle` tool**. It would call the existing [`BundleValidator.ValidateBundle`](../../src/AutoGIS.Civil3D.Handoff/BundleValidator.cs) in a separate .NET process and return the existing [`ValidationReport`](../../src/AutoGIS.Civil3D.Handoff/Validation/ValidationReport.cs) as bounded structured data. It requires no Civil 3D process, Autodesk assemblies, drawing, NETLOAD, or in-process network listener. It validates the package contract; it does not claim import success.

This is the smallest integration with an immediate repository use: an agent can check a staged handoff ZIP before an owner attempts native import. No candidate source needs to be copied or forked. The official [C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md/) has a local stdio host path. Proposed direct dependencies are [`ModelContextProtocol` 2.2.0, Apache-2.0](https://www.nuget.org/packages/ModelContextProtocol/2.2.0) and [`Microsoft.Extensions.Hosting` 10.0.10, MIT](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.10), both usable from .NET 8. These are evaluated version candidates; refresh advisories and supported patch versions before approving central pins and locked restore, and retain required distribution notices. The SDK's advertised read-only annotation is metadata, not a security boundary; the implementation must enforce read-only behavior itself.

The initial framework matches the existing validator. Revisit it before release: [Autodesk's transition guidance](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Civil-3D-Requirements-for-products-affected-by-the-Microsoft-NET-10-transition.html) records .NET 8 support ending on November 10, 2026. The out-of-process server can use a supported runtime independently of the adapter's separately approved native target; do not make a new deployment depend on an unsupported runtime.

## Options considered

| Option | Decision and reason |
| --- | --- |
| Install an existing Civil 3D MCP server | Defer. None has a source-backed, independently reproduced 2026 native result with this repository's host version and approval rules. Broad tool sets increase the native qualification scope. |
| Maintain a fork of Sacred-G or hjlrosales | Defer. Sacred-G needs a native authorization boundary; hjlrosales needs 2026 packaging, confirmation, and licensed validation. A fork would inherit an additional plugin and maintenance surface before this repo has a proven use for it. |
| Add MCP access around the existing adapter | Reserve for M3. The adapter has no generic bridge today; native inspection needs an approved workflow and host-update qualification. Reusing its entry point then avoids a second in-process plugin. |
| Expose existing Autodesk-free functionality through a small server | Preferred first implementation, subject to M1 design approval. It reuses the accepted validator and makes no native compatibility claim. |
| Operate without a new MCP server | Current state. The existing CLI already validates packages when an agent can run local commands; M1 is worthwhile only if a stable MCP tool interface is wanted by the owner. |

## Proposed interface and trust boundary

| Item | Proposed behavior |
| --- | --- |
| Transport | Child process over stdio, launched explicitly by the MCP client. Protocol only on stdout; diagnostics on stderr. No HTTP/TCP listener, Civil 3D plugin, or background service. |
| Tool | `validate_handoff_bundle(bundle_relative_path: string)`. Require a nonempty string and reject unknown arguments. Publish input and output schemas. One operation; no generic file reader, C# execution, command sender, or assembly loader. |
| File scope | Require `AUTOGIS_MCP_BUNDLE_ROOT` at launch. Accept only a relative `.zip` path below that approved local root. Reject missing root, absolute/UNC/device or drive-relative paths, alternate data streams, traversal, and reparse points in the root or any path component; verify directory-boundary containment before opening. The user chooses an owner-controlled local staging root containing only intended packages. |
| Response | Preserve `ValidationReport` status as `Valid`, `ValidWithWarnings`, or `Invalid`, its issue codes/severities, and verified metadata. Propose a 64 KiB UTF-8 response ceiling with an explicit `truncated` flag and total issue count; never turn truncation into a valid result. Bound and sanitize message/location/name strings as well as metadata. Omit raw ZIP content, environment variables, host paths, and stack traces. Treat returned package strings as untrusted data, never instructions. |
| Failure | Missing/unreadable file, invalid package, and server fault are distinct structured results. A package failing validation is a successful tool invocation with an invalid report, not a silently repaired package. |
| Work lifetime | Allow one validation at a time and reject concurrent calls with a stable busy result; do not queue unbounded work. `ValidateBundle(string)` is synchronous and has no cancellation argument. Cancel before starting when possible; once running, do not claim that client cancellation or timeout stopped the read. Discard the cancelled result, retain the busy state until completion, and let the client terminate/restart its child process when a hard stop is needed. No automatic retries. |
| Audit | Log tool name, outcome and duration to stderr without file contents or credentials. Do not persist drawing or package data. |

The process runs with the user's ordinary file rights. The path policy limits tool requests; it does not sandbox the process or defend against another process running as that user. In particular, the validator opens a path itself, so pre-open checks alone cannot defeat a concurrent junction/path replacement. M1 assumes the owner-controlled staging tree stays stable during a call; if hostile concurrent writers are in scope, design and test handle-based confinement before claiming that stronger boundary. Restricting output also keeps package metadata from being needlessly copied into model context.

## Delivery sequence

### M1 — Autodesk-free handoff check

1. Record the owner's phase placement and authorization in the roadmap gate log, then approve the one-tool interface, allowed bundle root, output shape, and read-only claim in a project design and implementation plan. Keep this proposal in `docs/plans/` until those decisions are made; the repository's approved design and plan directories have a separate meaning.
2. Add a small `src/AutoGIS.Civil3D.Mcp/` .NET 8 executable that references `AutoGIS.Civil3D.Handoff`. Use the official MCP C# stdio host and one tool method. Pin its dependencies in [`Directory.Packages.props`](../../Directory.Packages.props) and the NuGet lockfile; register it in the solution. Do not move validation rules into the MCP layer or parse the [CLI](../../src/AutoGIS.Civil3D.Handoff.Cli/CliApplication.cs) text output.
3. Add focused protocol/boundary coverage under `tests/AutoGIS.Civil3D.Mcp.Tests/`, using the existing test stack and fixture corpus: initialize/list/call with valid, warning, and invalid packages; reject missing root, malformed arguments, sibling-prefix traversal, device/drive-relative paths, alternate streams, and root/ancestor/file reparse points. Assert unchanged input hash and directory inventory, bounded/redacted output, and protocol-only stdout. Exercise busy, cancellation/disconnect, and child restart behavior without claiming cooperative cancellation inside the validator.
4. Restore in locked mode and run the solution tests. Record the MCP package version, lockfile hash, test output, and exact request/response. A passing M1 is **package validation only**; no Civil 3D native claim follows from it.

The file map is intentionally small: a project file, program, tool handler, lockfile, solution/package references, one focused test file, and a short local configuration note. Add a separate path helper only if the Windows containment check cannot remain clear in the handler. No shared bridge abstraction or plugin scaffolding is needed for one tool.

### M2 — Advisory proposal preview, if requested

Under a separate approved input/output design consistent with the [New Proposal workflow](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md), consider a second host-free tool that invokes the existing [`ProposalPlanner.Build`](../../src/AutoGIS.Civil3D.Proposal/ProposalPlanner.cs). It may return plan facts and warnings. It must not mint an execution approval, create files, or present the disabled Create path as complete. Its pure planning behavior can be checked in CI independently of native preview qualification; no host readiness or template qualification follows from it. Otherwise stop at M1.

### M3 — Native read-only inspection, only for an approved workflow

The [roadmap](../roadmap.md) and [ADR-0010](../adr/0010-production-setup-inspection-placement.md) govern when Survey Preflight or Audit EG Surface can become an implementation task. Resolve the installed 2026 update and in-process .NET target through [issue #123](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/123) first. Then add a narrowly named operation to the existing [`AutoGIS.Civil3D.Adapter`](../../src/AutoGIS.Civil3D.Adapter/) rather than load a second third-party plugin.

If an out-of-process client is needed, prefer a current-user, process-specific named pipe with a bounded request queue, explicit native-side operation allowlist, and no arbitrary expression, script, command, or assembly-loading endpoint. A handshake must identify the adapter build, protocol version, host process, and supported operations; fail on incompatible versions rather than silently attach to another instance. Marshal each native call into Autodesk's command context, verify the intended active document immediately before access, acquire required locks, open objects only for reading, and return a stale-document or busy result instead of switching drawings or retrying invisibly. Bind each request to one host process and document identity; test disconnect/reconnect, cancellation, timeouts, no-open-drawing behavior, and multiple instances before claiming reliability. A current-user pipe does not distinguish trusted humans from other processes under that account; the operation allowlist must enforce the read-only claim. MCP tool annotations alone cannot enforce these boundaries.

Qualify M3 through computer use on a licensed Civil 3D 2026 host and a disposable drawing: record the exact update/assemblies, loading behavior, request/result, relevant object properties and identifiers, and drawing dirty state before and after. An unchanged dirty flag alone cannot prove read-only behavior. Existing [preview/smoke guidance](../new-proposal-preview.md) and [issue #105](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/105) remain separate native gates. A managed test or a successful `AUTOGISPROPOSALSMOKE` command does not prove the new operation.

### M4 — Controlled writes, only after a separate design decision

Consider one named action only after the corresponding repository workflow has a qualified native executor, verification, receipt, and failure path. An owner-approved MCP write design must bind human approval to the exact plan digest, document identity where applicable, operation, inputs, and manifest/template fingerprints; the agent cannot approve its own action or mint an approval token. Recheck those bindings inside the adapter immediately before mutation. Use one-shot authorization and never auto-retry a timed-out write. Reconcile an uncertain result against the run identity and published receipt before offering another action.

For New Proposal, reuse the [receipt lifecycle approved in PR #129](https://github.com/0bnoxide/AutoGIS-Civil3D/pull/129), as specified in the [design](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md#execution-lifecycle) and [implementation plan](../superpowers/plans/2026-09-04-new-proposal-implementation-plan.md). After native verification and handle closure, prepare, flush, and close the success receipt in owned staging; promote it together with the project without overwrite. Before publication, cleanup is limited to owned generated content and failure evidence remains outside staging. After publication, presentation failure preserves the completed project and success receipt and reports a warning. Do not replace this lifecycle with CAD Undo or delete a published project because a reply was lost. Qualify faults at execution, receipt preparation, promotion, and presentation in disposable output. Other drawing-write workflows need their own tested transaction/undo recovery design. Broader design writes remain subject to their roadmap gate; the M1/M3 work does not advance it.

## Where each check runs

| Environment | Scope |
| --- | --- |
| Cloud or hosted CI | Locked restore, managed tests, MCP stdio protocol and path-boundary checks, source and dependency review. Autodesk package compilation is useful but does not prove native loading. |
| Local Windows without a licensed open Civil 3D session | Repeat managed and protocol checks; verify Windows reparse-point rejection and capture exact installed file versions. No drawing behavior can be claimed. |
| Licensed Windows Civil 3D 2026 with a disposable drawing | M3/M4 `NETLOAD` or autoload, native request/response, active-document changes, dirty-state comparison, disconnect/timeouts, and controlled rollback evidence. Native checks use computer use and preserve the working installation and drawings. |

## Decision gates and evidence

| Gate | Evidence needed | Decision owner |
| --- | --- | --- |
| M1 start | Explicit phase placement and gate-log authorization, plus approved one-tool design and implementation plan covering bundle root, response, and dependency/license review. | Repository owner through the roadmap process. |
| M1 completion | Locked restore, managed tests, real stdio request/response, path/output limits, unchanged inputs, and cancellation/restart behavior under the declared staging-root trust model. | Code review; no native qualification claim. |
| M3 start | Approved inspection workflow, exact Civil 3D 2026 update/.NET target, adapter load and licensing path. | Repository owner and current roadmap/issue gate. |
| M3 completion | Licensed desktop run through computer use with disposable drawing, repeatable tool trace, unchanged drawing state. | Native qualification owner. |
| M4 start | Separately approved write design plus verified staged executor/receipt and user-facing approval flow. | Repository owner; explicit authorization for the native write pilot. |

Installation and rollback are deliberately simple: M1 is enabled by an explicit local MCP client entry and removed by deleting that entry and stopping its child process. M3 must be loaded only in a disposable host session; closing that session unloads the experimental adapter path. No production drawings or persistent Civil 3D installation changes are part of this plan. Record any native failure as a blocker or product defect according to the evidence; a missing license or unavailable host does not count as a pass.

## Open decisions

- Whether the owner wants the M1 package check as the first MCP capability, and which local staging root may be exposed to it.
- Which exact installed Civil 3D 2026 update is the native target after the .NET 10 transition decision.
- Which approved inspection workflow, if any, merits M3 once native preview qualification is complete.

Until those decisions are made, the recommended operational state remains **no Civil 3D MCP server installed**. The actionable next step is review of the M1 interface, followed by a narrow implementation task if accepted.
