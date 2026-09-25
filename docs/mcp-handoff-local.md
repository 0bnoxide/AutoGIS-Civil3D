# Local handoff and proposal preview MCP

This opt-in .NET 8 stdio server exposes two read-only tools:

| Tool | Purpose |
| --- | --- |
| `validate_handoff_bundle` | Validate a handoff ZIP with the managed `BundleValidator` (M1). |
| `preview_proposal` | Return an advisory New Proposal action plan from supplied inputs and the owner-selected standards manifest (M2). |

Build from the repository root:

```powershell
dotnet restore src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj --locked-mode
dotnet build src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj -c Release --no-restore
```

For a local Codex client, this example uses only checked-in synthetic fixtures:

```powershell
$repoRoot = (Resolve-Path -LiteralPath .).Path
$bundleRoot = Join-Path $repoRoot 'fixtures\v1'
$manifest = Join-Path $repoRoot 'tests\AutoGIS.Civil3D.Proposal.Tests\Fixtures\synthetic-standards.json'
$serverDll = Join-Path $repoRoot 'src\AutoGIS.Civil3D.Mcp\bin\Release\net8.0\AutoGIS.Civil3D.Mcp.dll'
codex mcp add autogis-handoff --env "AUTOGIS_MCP_BUNDLE_ROOT=$bundleRoot" --env "AUTOGIS_MCP_STANDARDS_MANIFEST=$manifest" -- dotnet exec "$serverDll"
```

For actual handoff ZIPs, set `AUTOGIS_MCP_BUNDLE_ROOT` to an absolute, owner-controlled staging directory. It has no default. Keep that directory stable during validation; path checks do not protect against another process replacing entries concurrently. `AUTOGIS_MCP_STANDARDS_MANIFEST` must point to an absolute local `.json` file selected by the owner before the child starts. It has no default, and callers cannot supply or change the manifest through tool arguments. At startup, the server reads up to 256 KiB plus one byte to detect oversize input, validates it once, and keeps the resulting snapshot for its lifetime. Restart the child to select or reload standards. Both tools remain listed when the manifest is unusable, and M1 remains available.

## Validate a handoff bundle

Call `validate_handoff_bundle` with one string argument, `bundle_relative_path`, for a ZIP beneath `AUTOGIS_MCP_BUNDLE_ROOT`:

```json
{"bundle_relative_path":"valid/known-vertical-datum.zip"}
```

The synthetic fixture returns:

```json
{"status":"Valid","issueCount":0,"truncated":false,"issues":[],"metadata":{"packageId":"9a8ff271-b0d8-46db-809d-a6f72954af20","surfaceName":"Existing Ground","pointCount":3,"faceCount":1,"epsgCode":26913}}
```

The structured result contains `status`, `issueCount`, a bounded `issues` list, `truncated`, and verified `metadata` when validation reaches that stage. Invalid ZIP content is a validation result. Configuration, path, file access, and server failures return short tool error codes. The server writes only protocol messages to stdout and diagnostics to stderr. Stop the child process to end a running validation if it does not finish promptly.

## Preview a proposal

Call `preview_proposal` with the five required `snake_case` fields shown below. Optional fields are `client_number`, `project_number`, `proposal_number`, `site_address`, and `project_manager`. Each supplied string is limited to 1,024 UTF-16 code units. Missing, extra, mistyped, or overlong arguments return `INVALID_ARGUMENTS`.

```json
{"client_name":"Client","site_name":"Site","proposal_year":2026,"orientation":"Landscape","sheet_size":"TEST-A1"}
```

With the synthetic manifest above, this produces an `AdvisoryPlan` with `manifestVersion: 1`, `proposedRootName: "Client - Site"`, `existingGround: "Pending"`, and the complete planner-ordered `actions` array of 36 actions. The first action is `{"id":"00:root","operation":"CreateFolder","relativePath":"","dependencies":[]}`. Every action has only those four fields. `actionCount` is the full array length; `issues` is empty. The flags are always `advisoryOnly: true`, `nativePreflightPerformed: false`, and `templatesChecked: false`.

For example, changing only `proposal_year` to `2030` with this synthetic manifest returns this complete `NoPlan` structured result:

```json
{
  "status": "NoPlan",
  "advisoryOnly": true,
  "nativePreflightPerformed": false,
  "templatesChecked": false,
  "manifestVersion": null,
  "proposedRootName": null,
  "existingGround": null,
  "actionCount": 0,
  "actions": [],
  "issues": [{"code":"PROPOSAL_UNSUPPORTED_YEAR","explanation":"Choose a year in configured standards."}]
}
```

A planner rejection is a successful `NoPlan` result. The entire projected result is limited to 64 KiB UTF-8; an oversized plan returns `PREVIEW_TOO_LARGE` without a partial action list. If the manifest cannot be used, M2 returns one of four fixed errors:

| Error | Meaning |
| --- | --- |
| `MANIFEST_NOT_CONFIGURED` | Setting is absent or is not an absolute local `.json` path. |
| `MANIFEST_UNREADABLE` | File is unavailable or cannot be read. |
| `MANIFEST_TOO_LARGE` | File exceeds 256 KiB. |
| `MANIFEST_INVALID` | Manifest parser rejects the file. |

This preview is advisory managed planning. It does not open referenced templates or proposal base roots, check template existence, run Civil 3D native preflight, inspect the active drawing, check output collisions or execution readiness, create files, issue an approval, or produce a receipt. Native New Proposal qualification remains tracked separately in [issue #105](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/105). Treat manifest-derived action labels as untrusted client data.

To roll back, run `codex mcp remove autogis-handoff`, then close or restart the client session to stop any running child. A stuck validation can also be stopped by ending its child process.
