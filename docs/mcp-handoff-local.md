# Local handoff validator MCP

This opt-in .NET 8 stdio server exposes one read-only tool, `validate_handoff_bundle`. It validates a handoff ZIP with the repository's managed `BundleValidator`. It does not start Civil 3D, inspect a drawing, or create geometry.

Build from the repository root:

```powershell
dotnet restore src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj --locked-mode
dotnet build src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj -c Release --no-restore
```

For a local Codex client, this example registers the built server against the checked-in fixtures:

```powershell
$repoRoot = (Resolve-Path -LiteralPath .).Path
$bundleRoot = Join-Path $repoRoot 'fixtures\v1'
$serverDll = Join-Path $repoRoot 'src\AutoGIS.Civil3D.Mcp\bin\Release\net8.0\AutoGIS.Civil3D.Mcp.dll'
codex mcp add autogis-handoff --env "AUTOGIS_MCP_BUNDLE_ROOT=$bundleRoot" -- dotnet exec "$serverDll"
```

For actual handoff ZIPs, set `AUTOGIS_MCP_BUNDLE_ROOT` to an absolute, owner-controlled staging directory instead. The server has no default root. Keep that directory stable while validation runs; path checks do not protect against another process replacing entries concurrently.

Call `validate_handoff_bundle` with one string argument, `bundle_relative_path`, such as `valid/known-vertical-datum.zip` when the root is `fixtures/v1`. Paths must stay under the configured root. The tool returns a structured validation status, issue count, bounded issue list, truncation flag, and verified metadata when validation reaches that stage. Invalid ZIP content is a validation result; configuration, path, file access, and server failures return short tool error codes. The server writes protocol messages to stdout and diagnostics to stderr. Stop the child process to end a running validation if it does not finish promptly.

To roll back, run `codex mcp remove autogis-handoff`, then close or restart the client session to stop any running child. A stuck validation can also be stopped by ending its child process.
