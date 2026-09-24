# Local handoff MCP validation — Implementation plan

**Status:** Approved scope by the owner's 2026-09-24 instructions to proceed with recommended M1 and extend Phase 4. Governed by [ADR-0011](../../adr/0011-phase4-local-handoff-mcp.md) and the [design](../specs/2026-09-24-local-handoff-mcp-design.md). **Do not write product code until the separate documentation-only reservation-removal change has merged.**

## Gate and scope

First merge the documentation-only change recording Phase 4 placement, the approved design and plan, and a roadmap reservation marker for `src/AutoGIS.Civil3D.Mcp/` and `tests/AutoGIS.Civil3D.Mcp.Tests/`. Then merge a separate documentation-only roadmap change removing the marker and appending the implementation authorization. The checker compares the marker in both the base and changed roadmap, so product code cannot share either change. After those gates, claim the implementation files in a fresh worktree from current main. Do not expand M1 into native inspection, proposal creation, or writes.

## Build the one-tool facade

1. Add `src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj`, a small `Program.cs`, and one tool handler. Reference the existing Handoff project directly. Register one stdio tool with the official `ModelContextProtocol` C# SDK; route all host logging to stderr. Pin the SDK and `Microsoft.Extensions.Hosting` centrally in [`Directory.Packages.props`](../../../Directory.Packages.props), add locked restores and solution entries. Check supported package patches and advisories at implementation time; do not infer release support from compilation.
2. Add `tests/AutoGIS.Civil3D.Mcp.Tests/` using the existing xUnit stack and fixture corpus. Write a failing test for each behavior before implementing it. Exercise the real child process with the SDK stdio client: initialize, list exactly the intended tool, call valid, warning, and invalid packages, and inspect structured output and protocol-only stdout. Keep direct unit tests for input/path rules that a process test cannot diagnose precisely.
3. Implement the path boundary against the launch-time `AUTOGIS_MCP_BUNDLE_ROOT`. Reject absent root and disallowed input before calling `ValidateBundle`. Use `Path.GetFullPath`, directory-separator containment, OS-appropriate comparison, and `FileAttributes.ReparsePoint` checks on root, ancestors and final file. Follow the repository's private fixture-builder containment logic as a reference, but do not depend on that tool. Tests cover sibling-prefix escape, `..`, UNC/device and drive-relative forms, alternate streams, wrong extension, and reparse points. Run only with an owner-controlled root because the validator reopens the path and pre-open checks have a concurrent-replacement limit.
4. Map `ValidationReport` into bounded, sanitized structured output. Preserve status and verified metadata; include issue count and explicit truncation within the 64 KiB proposed UTF-8 ceiling. Package-invalid is a result, while bad path, missing file and server failure return distinct safe errors. Tests assert no host path, ZIP content, environment value, or stack trace appears, and compare file hash and directory inventory before/after.
5. Serialize validation with one busy guard. The validator is synchronous: cancellation before start can stop a call; cancellation during work discards its result but does not pretend to stop the read. Keep busy until it ends. Test concurrent calls, disconnect/timeout behavior, and a fresh child-process restart. Never auto-retry a validation.
6. Add a short local configuration note with an explicit root setting and removal step. Do not change the user's global MCP settings or working Civil 3D installation as part of the code change.

## Verification and acceptance

- Run `dotnet restore AutoGIS.Civil3D.sln --locked-mode`, build and full solution tests in Release, and repository documentation checks. Commit each new project's generated `packages.lock.json`.
- Capture a real stdio request/result trace from the built server, with package paths and contents omitted from shared evidence. Record package versions and lockfile hashes.
- Confirm the tool's input/output schemas, invalid/warning/valid distinctions, path and output bounds, unchanged package bytes, busy/cancellation/restart behavior, and no stdout noise.
- Report M1 as Autodesk-free package validation only. A managed test, compile, or protocol trace does not pass Civil 3D native qualification or contract-package import.

The first delivery is a reviewable local server and configuration example. Enabling it is an explicit client entry with an owner-selected staging root; deleting that entry and stopping the child process reverses the installation. Future M2–M4 work remains under the [evaluation plan](../../plans/civil3d-2026-mcp-integration-plan.md) and needs its own decisions.
