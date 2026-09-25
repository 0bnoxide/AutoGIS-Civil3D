# Phase 4 M2 advisory proposal preview — Design

**Status:** Design direction and written spec approved by the owner on 2026-09-24. This design does not authorize product changes. The [roadmap](../../roadmap.md) must record M2's Phase 4 scope and complete its documentation gate before implementation.

## Purpose and boundary

Give an agent an inspectable, Autodesk-free preview of what the existing New Proposal planner would propose for supplied inputs and one owner-selected standards manifest. M2 adds one read-only `preview_proposal` tool to the existing local stdio MCP child process. It calls [`StandardsManifest.Parse`](../../../src/AutoGIS.Civil3D.Proposal/StandardsManifest.cs) and [`ProposalPlanner.Build`](../../../src/AutoGIS.Civil3D.Proposal/ProposalPlanner.cs) without changing either library or the M1 handoff validator.

The result is **advice**, not the native New Proposal preview. It does not check template existence, Civil 3D version or licensing, active drawing, output collisions, or execution readiness. It never creates files, issues a `ProposalApproval`, invokes a Create path, or produces a receipt. The existing Phase 4 exit gate and Phase 5/6 statuses do not change.

## Inputs and manifest selection

`preview_proposal` accepts exactly the existing `ProposalInputs` fields, using `snake_case` MCP arguments: required `client_name`, `site_name`, `proposal_year`, `orientation`, and `sheet_size`; optional `client_number`, `project_number`, `proposal_number`, `site_address`, and `project_manager`. Missing, extra, mistyped, or overlong arguments fail with `INVALID_ARGUMENTS`. Each supplied string is limited to 1,024 UTF-16 code units before the core's stricter validation runs. An omitted optional field is `null`.

The owner sets `AUTOGIS_MCP_STANDARDS_MANIFEST` to an absolute local `.json` file before launching the child process. The tool accepts no manifest bytes or path from the caller and does not choose a default. At startup, read at most 256 KiB plus one byte, parse the bytes once, and retain the validated immutable manifest snapshot for that process lifetime. Restart to select or reload standards. Both tools remain discoverable in `tools/list` even when M2 has no usable manifest. An M2 call then returns only `MANIFEST_NOT_CONFIGURED` (missing or invalid setting), `MANIFEST_UNREADABLE` (file unavailable or read failure), `MANIFEST_TOO_LARGE`, or `MANIFEST_INVALID` (parser rejection). M1 remains usable. No referenced template or proposal base root is opened. The configured file is an operator-selected input, not a sandbox boundary against another process running as the same user.

## Advisory output

A successful call returns bounded structured content with this shape:

| Field | Meaning |
| --- | --- |
| `status` | `AdvisoryPlan` or `NoPlan` |
| `advisoryOnly`, `nativePreflightPerformed`, `templatesChecked` | Always `true`, `false`, `false` respectively |
| `manifestVersion`, `proposedRootName`, `existingGround` | Version, validated leaf name, and `Pending` when a plan exists; otherwise `null` |
| `actionCount`, `actions` | Total and **complete** planner-ordered action list when a plan exists; otherwise zero and empty |
| `issues` | Stable planner issue codes with fixed safe explanations; empty when a plan exists |

Each action contains only `id`, `operation`, proposal-root-relative `relativePath`, and ordered `dependencies`. Preserve the planner's order, including the root `CreateFolder` action whose `relativePath` is the empty string. Do not serialize `ProposalPlan.ToJson()`, action `Data`, `Configuration`, `FinalRoot`, base roots, template paths, property values, raw issue messages, or raw issue locations. The tool does not return an approval token or a plan digest that could be mistaken for one. Package- or manifest-derived strings remain untrusted data for the MCP client.

Serialize the **entire** projected result within 64 KiB UTF-8. If it would exceed the limit, return `PREVIEW_TOO_LARGE` without a partial action graph. The MCP output schema and read-only annotation describe this contract; server code enforces it. A planner rejection is a successful `NoPlan` tool result with issue codes, as an invalid handoff bundle is a result in M1. Configuration, argument, size, and unexpected server failures return fixed safe error codes with no host path, manifest text, environment value, or stack trace.

## Implementation and qualification boundary

Reuse the existing MCP SDK host and add one focused proposal handler plus a reference to `AutoGIS.Civil3D.Proposal`. Keep M1's root, busy, cancellation, and response behavior intact. Do not reuse its ZIP-specific path checker or `SafeLabel` for proposal-relative paths. No native adapter, shared bridge framework, new dependency, or global MCP registration is needed.

The implementation tests must exercise real stdio initialize/list/call with exactly both intended tools; a synthetic manifest with nonexistent templates and base roots; valid and rejected inputs; unsupported year and sheet profile; missing, unreadable, oversized, and malformed manifest; manifest snapshot stability until restart; exact deterministic action projection; oversize rejection without partial output; bounded and redacted output; unchanged input bytes and directory inventory; M1 regression; and protocol-only stdout. Run locked restore, full managed tests, and documentation checks. These tests establish host-free planning only. Native preview issue [#105](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/105) and the New Proposal workflow gate remain separate.

## Gate and delivery

The documentation-only M2 gate change records the owner's Phase 4 decision, this reviewed design, and an approved implementation plan. It also includes an ADR number allocated through the coordination tool that records M2's narrow host-free acceptance exception to the New Proposal design's live-Civil-3D-per-milestone rule; native New Proposal qualification remains required. It temporarily reserves `src/AutoGIS.Civil3D.Mcp/` and `tests/AutoGIS.Civil3D.Mcp.Tests/` in the roadmap marker. A separate documentation-only change removes that marker and records implementation authorization; only after it merges may the source/test PR begin. Each change receives the repository's proportionate independent review and green checks. No M2 source or test file shares a commit with marker addition or removal.

## Alternatives and prior art

The selected launch-time manifest snapshot keeps standards under owner control and avoids sending proprietary manifest contents through tool arguments. Per-call manifest input was rejected because it gives the caller standards selection and increases model-context exposure. Counts-only output was rejected because it cannot show which actions or relative paths the planner proposes. An incomplete paged preview is deferred; a 64 KiB limit with a clear error is easier to assess.

This design follows the approved [New Proposal architecture](2026-09-04-civil-production-accelerator-design.md), the [M1 MCP design](2026-09-24-local-handoff-mcp-design.md), and the [MCP evaluation plan](../../plans/civil3d-2026-mcp-integration-plan.md). It copies no third-party bridge code.
