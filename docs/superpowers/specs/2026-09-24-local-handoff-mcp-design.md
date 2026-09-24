# Local handoff MCP validation — Design

**Status:** Approved 2026-09-24 by the owner's instruction to implement the recommended M1 and place it in Phase 4. [ADR-0011](../../adr/0011-phase4-local-handoff-mcp.md) records that exception; the [evaluation](../../research/civil3d-2026-mcp-evaluation.md) contains candidate evidence.

## Purpose and boundary

Give an agent one repeatable preflight check of an AutoGIS contract ZIP before a person attempts Civil 3D import. Use the existing [`BundleValidator`](../../../src/AutoGIS.Civil3D.Handoff/BundleValidator.cs) and its [`ValidationReport`](../../../src/AutoGIS.Civil3D.Handoff/Validation/ValidationReport.cs). Contract validity is the only outcome. The server must not start Civil 3D, load an Autodesk assembly, inspect or change a drawing, repair a package, or issue execution approval.

The MCP server is a separate .NET 8 child process over stdio. It contains one named tool, `validate_handoff_bundle`, and references the handoff library directly. No TCP/HTTP listener, in-process plugin, generic file reader, command sender, or C# execution endpoint is included. Logging goes to stderr; stdout carries MCP protocol messages only. Use the official C# SDK with pinned, locked dependencies. A later release must recheck the supported runtime before .NET 8 support ends.

## Tool contract

Input is one nonempty `bundle_relative_path` string. Reject unknown arguments, absolute paths, UNC/device paths, drive-relative paths, traversal, alternate streams, non-`.zip` names, and path components that are reparse points. `AUTOGIS_MCP_BUNDLE_ROOT` is required at launch with no fallback. Resolve and verify directory-boundary containment under that owner-controlled local staging root before validation. The root and its contents are assumed stable against concurrent replacement during a call; pre-open path checks cannot secure a tree that a hostile same-user process can modify concurrently.

Return structured status (`Valid`, `ValidWithWarnings`, or `Invalid`), bounded issues (code, severity, sanitized message and package-relative location), and verified package metadata. Keep a proposed 64 KiB UTF-8 response ceiling with total issue count and an explicit `truncated` flag. Truncation must never imply validity or erase the actual status. Do not expose absolute host paths, raw ZIP content, environment variables, or stack traces. Strings extracted from a package are data, never instructions. Publish input and output schemas, but enforce boundaries in server code; an MCP read-only annotation is descriptive only.

An invalid package is a successful tool call with `Invalid` status. Missing root, disallowed path, unreadable file, and server fault return distinct safe errors. The synchronous validator has no cancellation argument. Run at most one validation at a time; reject concurrent calls as busy, do not queue unbounded work, and do not automatically retry. Cancellation before work starts can refuse it. If cancellation arrives during validation, discard the result and remain busy until the synchronous call finishes; terminating the child process is the hard-stop option.

## Qualification and rollout

Managed CI proves locked restore, actual stdio initialize/list/call behavior, valid/warning/invalid fixture results, path rejection, bounded/redacted output, busy/cancellation behavior, and unchanged input bytes and directory inventory. Windows checks cover root, ancestor, and final-file reparse points. These checks do not qualify a Civil 3D host or native import.

Ship an example local MCP client entry and instructions for choosing the staging root; do not silently register a global server or pick a user directory. Removing that entry and stopping the child process rolls back M1. Native read-only inspection and controlled writes remain separate decisions under the [integration plan](../../plans/civil3d-2026-mcp-integration-plan.md).

## Prior art

The [candidate evaluation](../../research/civil3d-2026-mcp-evaluation.md) explains why a small facade is preferred to copying a public bridge. The [official C# SDK getting-started guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/getting-started.md) provides the stdio host pattern; the SDK's [tool documentation](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/tools/tools.md) describes typed structured output. No third-party candidate code is copied.
