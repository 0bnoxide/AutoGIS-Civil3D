# Phase 4 M2 advisory proposal preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one read-only `preview_proposal` tool to the existing local MCP child process that returns a bounded, redacted projection of the pure New Proposal planner.

**Architecture:** Load one capped standards manifest at process start, call the unchanged `ProposalPlanner.Build`, and project only advisory fields. Keep M1's handler and the Civil 3D adapter independent. Register M2 even when its manifest is unavailable so M1 remains usable.

**Tech Stack:** .NET 8, the pinned ModelContextProtocol 2.2.0 SDK, System.Text.Json, existing xUnit protocol harness and synthetic proposal fixture. Add only a project reference to `AutoGIS.Civil3D.Proposal`.

**Spec:** [Phase 4 M2 advisory proposal preview design](../specs/2026-09-24-phase4-m2-proposal-preview-design.md). [ADR-0012](../../adr/0012-phase4-m2-advisory-preview.md) records the Phase 4 placement and host-free acceptance boundary when the documentation gate is merged.

**Status:** Written design and plan approved by the owner on 2026-09-24, with subagent-driven execution selected. Product edits remain blocked until the separate documentation-only reservation-removal PR merges.

## File Map

| File | Responsibility |
| --- | --- |
| `src/AutoGIS.Civil3D.Mcp/Program.cs` | Construct the one launch-time M2 handler and register its tool beside M1. |
| `src/AutoGIS.Civil3D.Mcp/ProposalTools.cs` | Own manifest snapshot, raw argument validation, planner call, safe projection, and M2 schema/model. |
| `src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj` | Add the existing Proposal project reference. |
| `src/AutoGIS.Civil3D.Mcp/packages.lock.json`, `tests/AutoGIS.Civil3D.Mcp.Tests/packages.lock.json` | Record the changed project graph without package upgrades. |
| `tests/AutoGIS.Civil3D.Mcp.Tests/ProposalPreviewProtocolTests.cs` | Exercise M2 over real stdio with synthetic standards and safe-output assertions. |
| `tests/AutoGIS.Civil3D.Mcp.Tests/McpProtocolTests.cs` | Update only the two M1-era one-tool list assertions. |
| `docs/mcp-handoff-local.md` | Describe the second tool, launch setting, and advisory limits. |
| `docs/roadmap.md`, `docs/adr/README.md`, `docs/adr/0012-phase4-m2-advisory-preview.md` | Record and enforce the documentation gate. |

## Global Constraints

- Before reading each workspace file, run the repository-required authenticated `sonar analyze secrets <path>`; stop if it reports a secret. Query codebase-memory MCP before new repository text searches. Run `coordination.py check --session 01a0d317-e56a-7670-9d22-8a4568617d1f` before writes in a claimed worktree; keep `main` read-only.
- Merge a documentation-only scope/reservation PR first, then a separate documentation-only reservation-removal PR, then begin the product PR from the resulting `origin/main`. The gate checker unions marker paths in base and changed roadmaps, so neither documentation PR may include M2 source or test edits.
- The configured manifest is a launch-time operator setting, not an argument. Read no template, base root, drawing, or other manifest-referenced path. Do not change the core planner, native adapter, Create flow, receipt flow, or approval model.
- All tool errors use fixed safe codes without raw exception, path, manifest, or input text. Planner rejection is successful `NoPlan`. An advisory result never claims native qualification or template checks.
- Keep each product task test first, then minimal code, then its focused test and a commit. Complete the full managed suite and independent review before the source PR merges. Issue #105 and the Phase 4 native gate remain open.

## Review Focus

1. **SDK dispatch escapes server validation.** Task 3's real stdio test sends missing, extra, and mistyped fields and requires the fixed `INVALID_ARGUMENTS` result.
2. **Manifest configuration disables M1 or causes later filesystem reads.** Task 4 tests both tools listed with bad M2 configuration and a working M1 call; Task 6 proves the startup snapshot persists after editing the manifest file.
3. **Preview leaks host paths or property values.** Task 6 compares the exact allowlisted projection and checks that root/template markers and input metadata are absent from serialized output.
4. **Large previews are silently truncated.** Task 6 builds a valid large synthetic manifest and requires only `PREVIEW_TOO_LARGE`, with no structured or partial action content.
5. **A documentation marker is bypassed by a mixed PR.** Tasks 1–2 run `docs_checks.py --root . --baseline origin/main` on each documentation-only diff and verify that source/test paths are untouched.

---

### Task 1: Record the approved M2 design behind a documentation reservation

**Files:** `docs/roadmap.md`, `docs/adr/README.md`, `docs/adr/0012-phase4-m2-advisory-preview.md`, this plan, and the M2 design. No `src/` or `tests/` files.

**Interfaces:** Consumes the owner's approved written M2 design and ADR allocation 0012. Produces a Phase 4 roadmap scope record and a blocking marker for the two M2 implementation directories.

- [ ] Keep the drafted roadmap, ADR index, ADR file, design, and plan in the isolated documentation worktree. ADR 0012 and the affected documentation paths are already claimed through `coordination.py`; verify those claims before every further edit. Do not merge the gate PR until the owner approves this written plan.
- [ ] Write ADR-0012 with Context, Decision, Alternatives, and Consequences. Record the approved Phase 4 advisory scope and the narrow exception to the governing New Proposal design's live Civil 3D run per milestone. The exception applies only to this Autodesk-free M2 planning slice; native New Proposal preview and Phase 4 exit qualification remain required.
- [ ] Link the design, plan, and ADR from the Phase 4 Delivery-level paragraph, append the owner's M2 design and plan approvals to the roadmap gate-change log, and insert this exact marker after the Capability-level table:

```text
<!-- docs-checks:phase-gate-v1 {"phase":4,"state":"blocked","paths":["src/AutoGIS.Civil3D.Mcp/","tests/AutoGIS.Civil3D.Mcp.Tests/"]} -->
```

- [ ] Run `python tools/checks/docs_checks.py --root . --baseline origin/main`, `git diff --check`, and inspect `git diff --name-only origin/main` to confirm the PR is documentation-only. Obtain independent adversarial review; make the draft PR ready only after the owner approves this plan, then merge with green hosted checks.

### Task 2: Remove the reservation in a separate documentation-only PR

**Files:** `docs/roadmap.md` only.

**Interfaces:** Consumes the merged Task 1 roadmap marker and owner approval of this plan. Produces a marker-free `origin/main` from which M2 source/test work may branch.

- [ ] After Task 1 merges, fetch the exact merged `origin/main` in a fresh claimed worktree. Append the owner's implementation authorization to the roadmap gate-change log and remove only the M2 marker line; preserve Phase 4 Authorized, Phases 5/6 Identified, and the Phase 4 exit gate.
- [ ] Run `python tools/checks/docs_checks.py --root . --baseline origin/main`, `git diff --check`, and inspect `git diff --name-only origin/main` for a documentation-only diff. Obtain independent review and merge with green hosted checks.
- [ ] Start the M2 source branch only after the marker-removal commit is present on `origin/main`. Claim `src/AutoGIS.Civil3D.Mcp/`, `tests/AutoGIS.Civil3D.Mcp.Tests/`, and `docs/mcp-handoff-local.md` in its isolated worktree.

### Task 3: Prove SDK schema dispatch before building the handler

**Files:** Modify `src/AutoGIS.Civil3D.Mcp/AutoGIS.Civil3D.Mcp.csproj`, `src/AutoGIS.Civil3D.Mcp/packages.lock.json`, `src/AutoGIS.Civil3D.Mcp/Program.cs`, `tests/AutoGIS.Civil3D.Mcp.Tests/packages.lock.json`, and `tests/AutoGIS.Civil3D.Mcp.Tests/McpProtocolTests.cs`; create `src/AutoGIS.Civil3D.Mcp/ProposalTools.cs` and `tests/AutoGIS.Civil3D.Mcp.Tests/ProposalPreviewProtocolTests.cs`.

**Interfaces:** Consumes `RequestContext<CallToolRequestParams>` and the existing M1 tool registration. Produces `ProposalTools.PreviewProposal(RequestContext<CallToolRequestParams>, CancellationToken): CallToolResult`, `ProposalTools.InputSchemaJson`, `ProposalTools.OutputSchemaJson`, and `ProposalPreviewOutput`; the handler accepts no typed user arguments. `ProposalTools.Error(string): CallToolResult` returns a single safe text code.

- [ ] Write a failing real child-process protocol test that lists exactly `preview_proposal` and `validate_handoff_bundle`, checks M2's ten-field input schema (`additionalProperties: false`, five required fields), `readOnlyHint`, and non-null output schema. Use the existing `McpProtocolTests.cs` stdio transport pattern in the new `ProposalPreviewProtocolTests.cs`.

```csharp
var names = (await client.ListToolsAsync()).Select(tool => tool.Name)
    .Order(StringComparer.Ordinal).ToArray();
Assert.Equal(new[] { "preview_proposal", "validate_handoff_bundle" }, names);
var preview = Assert.Single((await client.ListToolsAsync())
    .Where(tool => tool.Name == "preview_proposal"));
Assert.False(preview.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());
Assert.Equal(5, preview.ProtocolTool.InputSchema.GetProperty("required").GetArrayLength());
Assert.True(preview.ProtocolTool.Annotations?.ReadOnlyHint);
Assert.NotNull(preview.ProtocolTool.OutputSchema);
```

- [ ] Write a second failing protocol test using `CallToolAsync` with missing, unknown, wrong-type, supplied optional null, and over-1,024-character arguments. Each result must have `IsError=true`, null structured content, and one `INVALID_ARGUMENTS` text block. Keep the test data in one `[Theory]` or loop so a failure identifies its input case.

```csharp
private static Dictionary<string, object?> ValidArguments => new()
{
    ["client_name"] = "Client",
    ["site_name"] = "Site",
    ["proposal_year"] = 2026,
    ["orientation"] = "Landscape",
    ["sheet_size"] = "TEST-A1"
};

private static string RepositoryRoot
{
    get
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
            dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tests",
                "AutoGIS.Civil3D.Proposal.Tests", "Fixtures",
                "synthetic-standards.json")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Proposal test fixture not found.");
    }
}
private static string SyntheticManifestPath => Path.Combine(RepositoryRoot,
    "tests", "AutoGIS.Civil3D.Proposal.Tests", "Fixtures",
    "synthetic-standards.json");
private static string FixtureRoot => Path.Combine(RepositoryRoot, "fixtures", "v1");
private static string ServerDllPath => Path.Combine(RepositoryRoot, "src",
    "AutoGIS.Civil3D.Mcp", "bin",
    new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net8.0",
    "AutoGIS.Civil3D.Mcp.dll");
private static async Task<McpClient> ConnectAsync(string? manifestPath,
    string? bundleRoot)
{
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "AutoGIS M2 protocol test",
        Command = "dotnet",
        Arguments = ["exec", ServerDllPath],
        EnvironmentVariables = new Dictionary<string, string?>
        {
            ["AUTOGIS_MCP_STANDARDS_MANIFEST"] = manifestPath,
            ["AUTOGIS_MCP_BUNDLE_ROOT"] = bundleRoot
        }
    });
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
}

var result = await client.CallToolAsync("preview_proposal",
    new Dictionary<string, object?> { ["client_name"] = "Client" });
Assert.Equal(true, result.IsError);
Assert.Null(result.StructuredContent);
Assert.Equal("INVALID_ARGUMENTS",
    Assert.Single(result.Content.OfType<TextContentBlock>()).Text);

var wrongType = ValidArguments;
wrongType["proposal_year"] = "2026";
var extra = ValidArguments;
extra["unexpected"] = true;
var optionalNull = ValidArguments;
optionalNull["project_manager"] = null;
var overlong = ValidArguments;
overlong["client_name"] = new string('x', 1025);
foreach (var args in new[] { wrongType, extra, optionalNull, overlong })
{
    CallToolResult rejected = await client.CallToolAsync("preview_proposal", args);
    Assert.Equal(true, rejected.IsError);
    Assert.Null(rejected.StructuredContent);
    Assert.Equal("INVALID_ARGUMENTS",
        Assert.Single(rejected.Content.OfType<TextContentBlock>()).Text);
}
```

- [ ] Run `dotnet test tests/AutoGIS.Civil3D.Mcp.Tests/AutoGIS.Civil3D.Mcp.Tests.csproj -c Release --filter FullyQualifiedName~ProposalPreviewProtocolTests` and confirm failure because M2 is not listed.
- [ ] Add a `<ProjectReference Include="../AutoGIS.Civil3D.Proposal/AutoGIS.Civil3D.Proposal.csproj" />` to the MCP project. Run `dotnet restore AutoGIS.Civil3D.sln -p:Civil3DYear=2026` once to update the source and test `packages.lock.json` files, inspect their diff for only the new Proposal project graph, then run `dotnet restore AutoGIS.Civil3D.sln --locked-mode -p:Civil3DYear=2026`.
- [ ] Add a context-only M2 handler and explicit schemas so the SDK does not deserialize ten typed user arguments before server validation. Define these output records in `ProposalTools.cs`:

```csharp
public sealed record PreviewAction(string Id, string Operation, string RelativePath,
    IReadOnlyList<string> Dependencies);
public sealed record PreviewIssue(string Code, string Explanation);
public sealed record ProposalPreviewOutput(string Status, bool AdvisoryOnly,
    bool NativePreflightPerformed, bool TemplatesChecked, int? ManifestVersion,
    string? ProposedRootName, string? ExistingGround, int ActionCount,
    IReadOnlyList<PreviewAction> Actions, IReadOnlyList<PreviewIssue> Issues);
```

- [ ] Declare `InputSchemaJson` and `OutputSchemaJson` as static raw JSON strings in `ProposalTools.cs`. The input schema has exactly the ten snake-case properties, `proposal_year` as `integer`, the other nine as `string`, five required names, and `additionalProperties: false`. The output schema has exactly the ten `ProposalPreviewOutput` fields in camel case, nested four-field action and two-field issue objects, nullable manifest/root/ground fields, and `additionalProperties: false` for each object. Use `JsonSerializer.Deserialize<JsonElement>` for both schema strings.

```csharp
internal const string InputSchemaJson = """
{
  "type": "object",
  "properties": {
    "client_name": {"type": "string"},
    "site_name": {"type": "string"},
    "proposal_year": {"type": "integer"},
    "orientation": {"type": "string"},
    "sheet_size": {"type": "string"},
    "client_number": {"type": "string"},
    "project_number": {"type": "string"},
    "proposal_number": {"type": "string"},
    "site_address": {"type": "string"},
    "project_manager": {"type": "string"}
  },
  "required": ["client_name", "site_name", "proposal_year", "orientation", "sheet_size"],
  "additionalProperties": false
}
""";
internal const string OutputSchemaJson = """
{
  "type": "object",
  "properties": {
    "status": {"type": "string", "enum": ["AdvisoryPlan", "NoPlan"]},
    "advisoryOnly": {"type": "boolean"},
    "nativePreflightPerformed": {"type": "boolean"},
    "templatesChecked": {"type": "boolean"},
    "manifestVersion": {"type": ["integer", "null"]},
    "proposedRootName": {"type": ["string", "null"]},
    "existingGround": {"type": ["string", "null"]},
    "actionCount": {"type": "integer"},
    "actions": {"type": "array", "items": {
      "type": "object",
      "properties": {
        "id": {"type": "string"},
        "operation": {"type": "string"},
        "relativePath": {"type": "string"},
        "dependencies": {"type": "array", "items": {"type": "string"}}
      },
      "required": ["id", "operation", "relativePath", "dependencies"],
      "additionalProperties": false
    }},
    "issues": {"type": "array", "items": {
      "type": "object",
      "properties": {
        "code": {"type": "string"},
        "explanation": {"type": "string"}
      },
      "required": ["code", "explanation"],
      "additionalProperties": false
    }}
  },
  "required": ["status", "advisoryOnly", "nativePreflightPerformed",
    "templatesChecked", "manifestVersion", "proposedRootName", "existingGround",
    "actionCount", "actions", "issues"],
  "additionalProperties": false
}
""";
```
- [ ] Create the M2 tool through `McpServerTool.Create` with `McpServerToolCreateOptions`; set `Name`, `ReadOnly`, `UseStructuredContent`, and explicit `OutputSchema`. Set `tool.ProtocolTool.InputSchema` to the input schema, then register it beside the existing M1 instance. Keep the handler's signature context-only.

```csharp
var preview = new ProposalTools(
    Environment.GetEnvironmentVariable("AUTOGIS_MCP_STANDARDS_MANIFEST"));
McpServerTool tool = McpServerTool.Create(
    new Func<RequestContext<CallToolRequestParams>, CancellationToken, CallToolResult>(
        preview.PreviewProposal),
    new McpServerToolCreateOptions
    {
        Name = "preview_proposal",
        Description = "Preview a New Proposal action plan from configured standards without creating files.",
        ReadOnly = true,
        UseStructuredContent = true,
        OutputSchema = JsonSerializer.Deserialize<JsonElement>(ProposalTools.OutputSchemaJson)
    });
tool.ProtocolTool.InputSchema = JsonSerializer.Deserialize<JsonElement>(ProposalTools.InputSchemaJson);
builder.Services.AddMcpServer().WithStdioServerTransport()
    .WithTools(new HandoffTools(Environment.GetEnvironmentVariable("AUTOGIS_MCP_BUNDLE_ROOT")))
    .WithTools([tool]);
```

- [ ] Implement exact key/type/length validation in `PreviewProposal` over `context.Params.Arguments`: four required strings, one required Int32 year, five optional strings only when present, no extra key, maximum 1,024 UTF-16 code units per supplied string. Return `Error("INVALID_ARGUMENTS")` for all failing cases. For a valid argument set at this stage, return `Error("MANIFEST_NOT_CONFIGURED")`. Define `Error` as `new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = code }] }`.

```csharp
public CallToolResult PreviewProposal(
    RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
{
    if (!TryReadInputs(context.Params.Arguments, out _))
        return Error("INVALID_ARGUMENTS");
    return Error("MANIFEST_NOT_CONFIGURED");
}

private static CallToolResult Error(string code) =>
    new() { IsError = true, Content = [new TextContentBlock { Text = code }] };
```

The Task 3 constructor accepts the operator setting for the later startup loader:

```csharp
public ProposalTools(string? manifestPath) => _configuredManifestPath = manifestPath;
private readonly string? _configuredManifestPath;
```

Use this exact validation seam in the same file; empty or otherwise unsafe strings reach the core planner and become `NoPlan`.

```csharp
private static readonly HashSet<string> InputKeys = new(StringComparer.Ordinal)
{
    "client_name", "site_name", "proposal_year", "orientation", "sheet_size",
    "client_number", "project_number", "proposal_number", "site_address",
    "project_manager"
};

private static bool TryReadInputs(IDictionary<string, JsonElement>? args,
    out ProposalInputs? inputs)
{
    inputs = null;
    if (args is null || args.Keys.Any(key => !InputKeys.Contains(key)) ||
        !ReadString(args, "client_name", true, out string? client) ||
        !ReadString(args, "site_name", true, out string? site) ||
        !ReadString(args, "orientation", true, out string? orientation) ||
        !ReadString(args, "sheet_size", true, out string? size) ||
        !ReadString(args, "client_number", false, out string? clientNumber) ||
        !ReadString(args, "project_number", false, out string? projectNumber) ||
        !ReadString(args, "proposal_number", false, out string? proposalNumber) ||
        !ReadString(args, "site_address", false, out string? address) ||
        !ReadString(args, "project_manager", false, out string? manager) ||
        !args.TryGetValue("proposal_year", out JsonElement rawYear) ||
        rawYear.ValueKind != JsonValueKind.Number ||
        !rawYear.TryGetInt32(out int year))
        return false;
    inputs = new(client!, site!, year, orientation!, size!,
        clientNumber, projectNumber, proposalNumber, address, manager);
    return true;
}

private static bool ReadString(IDictionary<string, JsonElement> args,
    string key, bool required, out string? value)
{
    value = null;
    if (!args.TryGetValue(key, out JsonElement raw))
        return !required;
    if (raw.ValueKind != JsonValueKind.String)
        return false;
    value = raw.GetString();
    return value is not null && value.Length <= 1024;
}
```
- [ ] Adjust the two old `Assert.Single` tool-list assumptions in `McpProtocolTests.cs` to expect both names while retaining every M1 status and stdout assertion. Run the focused suite green. If the SDK rejects context-only creation or schema assignment, resolve that API seam before adding manifest and planner logic; keep the tested wire contract.
- [ ] Commit the schema/dispatch slice. Do not add an adapter, shared bridge abstraction, or new package.

### Task 4: Snapshot and validate the manifest at startup

**Files:** Modify `src/AutoGIS.Civil3D.Mcp/ProposalTools.cs` and `tests/AutoGIS.Civil3D.Mcp.Tests/ProposalPreviewProtocolTests.cs`.

**Interfaces:** Consumes `AUTOGIS_MCP_STANDARDS_MANIFEST` at `ProposalTools(string? manifestPath)` construction. Produces immutable `_manifest: StandardsManifest?` and `_manifestError: string?`, used by Tasks 5–6.

- [ ] Write a failing `ProposalPreviewProtocolTests` table for absent/relative/non-`.json` settings and Windows UNC/device or `//` paths (`MANIFEST_NOT_CONFIGURED`), missing or unreadable files (`MANIFEST_UNREADABLE`), 256 KiB + 1 byte (`MANIFEST_TOO_LARGE`), and malformed UTF-8/JSON or parser rejection (`MANIFEST_INVALID`). Each child must still list both tools. With bad M2 configuration, an M1 valid-bundle call still succeeds.

```csharp
await using McpClient client = await ConnectAsync(manifestPath, FixtureRoot);
Assert.Equal(2, (await client.ListToolsAsync()).Count);
CallToolResult error = await client.CallToolAsync("preview_proposal", ValidArguments);
Assert.Equal(true, error.IsError);
Assert.Null(error.StructuredContent);
Assert.Equal(expectedCode, Assert.Single(error.Content.OfType<TextContentBlock>()).Text);
CallToolResult m1 = await client.CallToolAsync("validate_handoff_bundle",
    new Dictionary<string, object?> {
        ["bundle_relative_path"] = "valid/known-vertical-datum.zip" });
Assert.NotEqual(true, m1.IsError);
```
- [ ] Run `dotnet test tests/AutoGIS.Civil3D.Mcp.Tests/AutoGIS.Civil3D.Mcp.Tests.csproj -c Release --filter FullyQualifiedName~ProposalPreviewProtocolTests` and confirm the new manifest cases fail against Task 3's `MANIFEST_NOT_CONFIGURED` response.
- [ ] Replace Task 3's constructor and remove `_configuredManifestPath`. Implement a startup-only `FileStream` read of at most `256 * 1024 + 1` bytes, classify the four configuration failures, and keep the parsed `StandardsManifest` or safe error in readonly fields. Call `StandardsManifest.Parse(bytes)` once. Check the setting is fully qualified and has `.json` extension; reject UNC/device and `//` paths before opening. Do not use M1's ZIP path resolver or `File.ReadAllBytes`.

```csharp
private const int MaxManifestBytes = 256 * 1024;
private readonly StandardsManifest? _manifest;
private readonly string? _manifestError;

public ProposalTools(string? manifestPath)
{
    if (!IsLocalJsonPath(manifestPath))
    {
        _manifestError = "MANIFEST_NOT_CONFIGURED";
        return;
    }
    try
    {
        using var stream = new FileStream(manifestPath!, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[MaxManifestBytes + 1];
        int used = 0;
        while (used < bytes.Length)
        {
            int read = stream.Read(bytes, used, bytes.Length - used);
            if (read == 0) break;
            used += read;
        }
        if (used > MaxManifestBytes)
        {
            _manifestError = "MANIFEST_TOO_LARGE";
            return;
        }
        ManifestResult parsed = StandardsManifest.Parse(bytes.AsSpan(0, used));
        _manifest = parsed.Manifest;
        _manifestError = parsed.Manifest is null ? "MANIFEST_INVALID" : null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or System.Security.SecurityException)
    {
        _manifestError = "MANIFEST_UNREADABLE";
    }
}
```

Use this path guard before opening, classifying invalid settings as `MANIFEST_NOT_CONFIGURED`:

```csharp
private static bool IsLocalJsonPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path) ||
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal))
        return false;
    try
    {
        return Path.IsPathFullyQualified(path) &&
            string.Equals(Path.GetExtension(path), ".json",
                StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
    {
        return false;
    }
}
```

- [ ] Change the handler to return the stored `MANIFEST_*` error before parsing call arguments when no snapshot is available. Run the focused suite green.
- [ ] Commit the startup snapshot and configuration behavior.

### Task 5: Map valid arguments and planner rejections

**Files:** Modify `src/AutoGIS.Civil3D.Mcp/ProposalTools.cs` and `tests/AutoGIS.Civil3D.Mcp.Tests/ProposalPreviewProtocolTests.cs`.

**Interfaces:** Consumes raw `CallToolRequestParams.Arguments` and Task 4's `_manifest`. Produces `ProposalInputs` and a `PlanResult` from the unchanged `ProposalPlanner.Build`.

- [ ] Add stdio tests for invalid optional blank, unsafe client/site name, unsupported year, an integer year outside the planner's 1–9999 range, and unsupported orientation/size. Task 3 already tests supplied optional null as `INVALID_ARGUMENTS`. Planner rejection yields `IsError != true`, `status: "NoPlan"`, null manifest/root/ground fields, zero actions, and only fixed issue code/explanation. Add one valid synthetic-fixture case that returns `AdvisoryPlan` with 36 actions.

```csharp
var unsupportedYear = ValidArguments;
unsupportedYear["proposal_year"] = 2030;
CallToolResult noPlan = await client.CallToolAsync("preview_proposal",
    unsupportedYear);
Assert.NotEqual(true, noPlan.IsError);
JsonElement body = Assert.IsType<JsonElement>(noPlan.StructuredContent);
Assert.Equal("NoPlan", body.GetProperty("status").GetString());
Assert.Equal(JsonValueKind.Null, body.GetProperty("manifestVersion").ValueKind);
Assert.Equal(0, body.GetProperty("actionCount").GetInt32());
Assert.Equal("PROPOSAL_UNSUPPORTED_YEAR",
    body.GetProperty("issues")[0].GetProperty("code").GetString());
```
- [ ] Run the filtered `ProposalPreviewProtocolTests` red. Reuse Task 3's `TryReadInputs` to build the existing record with null omitted optionals; pass Int32 years outside 1–9999 to the planner for `NoPlan`:

```csharp
if (_manifestError is { } code) return Error(code);
if (!TryReadInputs(context.Params.Arguments, out ProposalInputs? inputs))
    return Error("INVALID_ARGUMENTS");
try
{
    PlanResult result = ProposalPlanner.Build(inputs!, _manifest!);
    return FormatResult(result);
}
catch (Exception)
{
    return Error("PREVIEW_FAILED");
}
```

- [ ] Implement `FormatResult(PlanResult): CallToolResult` with the ten-field `ProposalPreviewOutput` record. For a plan, project only `Id`, `Operation.ToString()`, `RelativePath`, and `Dependencies` from each action in planner order; the root action keeps `RelativePath == ""`. For `NoPlan`, use null manifest/root/ground, zero actions, and safe issue codes/explanations. Ignore `ProposalIssue.Message` and `Location` entirely; map unknown codes to `PROPOSAL_ISSUE`.

```csharp
private static CallToolResult FormatResult(PlanResult result)
{
    ProposalPlan? plan = result.Plan;
    PreviewAction[] actions = plan is null ? [] :
        plan.Actions.Select(a => new PreviewAction(a.Id, a.Operation.ToString(),
            a.RelativePath, a.Dependencies.ToArray())).ToArray();
    PreviewIssue[] issues = plan is null
        ? result.Issues.Select(SafeIssue).ToArray() : [];
    var output = new ProposalPreviewOutput(
        plan is null ? "NoPlan" : "AdvisoryPlan", true, false, false,
        plan?.ManifestVersion, plan is null ? null : plan.FinalRootComponents[1],
        plan is null ? null : ExistingGroundState.Pending.ToString(),
        actions.Length, actions, issues);
    JsonElement body = JsonSerializer.SerializeToElement(output,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    return new CallToolResult { Content = [], StructuredContent = body };
}

private static PreviewIssue SafeIssue(ProposalIssue issue) =>
    issue.Code switch
    {
        ProposalIssueCodes.InvalidInputs =>
            new(issue.Code, "Check proposal input values."),
        ProposalIssueCodes.UnsupportedYear =>
            new(issue.Code, "Choose a year in configured standards."),
        ProposalIssueCodes.UnsupportedSheet =>
            new(issue.Code, "Choose a configured sheet profile."),
        ProposalIssueCodes.UnsafePath =>
            new(issue.Code, "A planned path is too long."),
        _ => new("PROPOSAL_ISSUE", "Proposal planning was rejected.")
    };
```

- [ ] Run the filtered `ProposalPreviewProtocolTests` green, then commit planner mapping and the full initial projection. Task 6 adds the required 64 KiB bound before merge.

### Task 6: Project and bound the complete advisory output

**Files:** Modify `src/AutoGIS.Civil3D.Mcp/ProposalTools.cs` and `tests/AutoGIS.Civil3D.Mcp.Tests/ProposalPreviewProtocolTests.cs`.

**Interfaces:** Consumes `PlanResult`. Produces `ProposalPreviewOutput` in `CallToolResult.StructuredContent`, or one safe tool error code with no structured content.

- [ ] Add a protocol test comparing every returned action, in order, with the independent `ProposalPlanner.Build` result on the synthetic fixture. Assert the first root action has `relativePath: ""`; each action has exactly `id`, `operation`, `relativePath`, and `dependencies`; the result has exactly the design's top-level fields. Assert `advisoryOnly=true`, `nativePreflightPerformed=false`, and `templatesChecked=false`.

```csharp
PlanResult expected = ProposalPlanner.Build(
    new ProposalInputs("Client", "Site", 2026, "Landscape", "TEST-A1"),
    StandardsManifest.Parse(File.ReadAllBytes(SyntheticManifestPath)).Manifest!);
JsonElement actual = Assert.IsType<JsonElement>(reply.StructuredContent);
JsonElement actions = actual.GetProperty("actions");
Assert.Equal(expected.Plan!.Actions.Length, actions.GetArrayLength());
Assert.Equal("", actions[0].GetProperty("relativePath").GetString());
for (int i = 0; i < actions.GetArrayLength(); i++)
{
    PlannedAction action = expected.Plan.Actions[i];
    Assert.Equal(new[] { "dependencies", "id", "operation", "relativePath" },
        actions[i].EnumerateObject().Select(p => p.Name)
            .Order(StringComparer.Ordinal).ToArray());
    Assert.Equal(action.Id, actions[i].GetProperty("id").GetString());
    Assert.Equal(action.Operation.ToString(), actions[i].GetProperty("operation").GetString());
    Assert.Equal(action.RelativePath, actions[i].GetProperty("relativePath").GetString());
    Assert.Equal(action.Dependencies,
        actions[i].GetProperty("dependencies").EnumerateArray()
            .Select(item => item.GetString()).ToArray());
}
```

- [ ] Add a snapshot test: launch with a copied synthetic manifest, call M2, replace the file with malformed content, call M2 in the same process, and require the same preview. Restart the child and require `MANIFEST_INVALID`. Compare file bytes and directory inventory before and after each call, with the intentional replacement as a separate baseline; no template or base-root existence is required.

```csharp
File.Copy(SyntheticManifestPath, snapshotPath);
await using (McpClient running = await ConnectAsync(snapshotPath, FixtureRoot))
{
    CallToolResult first = await running.CallToolAsync("preview_proposal", ValidArguments);
    File.WriteAllText(snapshotPath, "{");
    CallToolResult second = await running.CallToolAsync("preview_proposal", ValidArguments);
    Assert.Equal(first.StructuredContent?.GetRawText(),
        second.StructuredContent?.GetRawText());
}
await using McpClient restarted = await ConnectAsync(snapshotPath, FixtureRoot);
CallToolResult invalid = await restarted.CallToolAsync("preview_proposal", ValidArguments);
Assert.Equal("MANIFEST_INVALID",
    Assert.Single(invalid.Content.OfType<TextContentBlock>()).Text);
```
- [ ] Add a redaction test with unique markers in manifest base roots/templates and optional metadata values. Assert no marker, `FinalRoot`, `Configuration`, action `Data`, raw issue message/location, approval token, or plan digest appears in serialized structured content.

```csharp
string raw = Assert.IsType<JsonElement>(reply.StructuredContent).GetRawText();
Assert.DoesNotContain("HOST_ROOT_MARKER", raw);
Assert.DoesNotContain("TEMPLATE_MARKER", raw);
Assert.DoesNotContain("OPTIONAL_METADATA_MARKER", raw);
Assert.DoesNotContain("finalRoot", raw, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("\"configuration\":", raw, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("\"data\"", raw, StringComparison.OrdinalIgnoreCase);
```
- [ ] Add a valid synthetic manifest with hundreds of extra root-level folder entries, under the 256 KiB input cap, whose complete projection exceeds 64 KiB UTF-8. Require `PREVIEW_TOO_LARGE`, `IsError=true`, no structured content, and no partial graph. Verify unchanged bytes and directory inventory.

```csharp
DirectoryInfo temp = Directory.CreateTempSubdirectory("AutoGIS-M2-Large-");
string largeManifestPath = Path.Combine(temp.FullName, "large.json");
JsonNode manifest = JsonNode.Parse(File.ReadAllText(SyntheticManifestPath))!;
JsonArray folders = manifest["folders"]!.AsArray();
for (int i = 0; i < 600; i++)
    folders.Add($"X{i:D4}{new string('a', 100)}");
File.WriteAllText(largeManifestPath, manifest.ToJsonString());
Assert.InRange(new FileInfo(largeManifestPath).Length, 1, 256 * 1024);
await using McpClient client = await ConnectAsync(largeManifestPath, FixtureRoot);
CallToolResult tooLarge = await client.CallToolAsync("preview_proposal", ValidArguments);
Assert.Equal(true, tooLarge.IsError);
Assert.Null(tooLarge.StructuredContent);
Assert.Equal("PREVIEW_TOO_LARGE",
    Assert.Single(tooLarge.Content.OfType<TextContentBlock>()).Text);
temp.Delete(recursive: true);
```

- [ ] Run `dotnet test tests/AutoGIS.Civil3D.Mcp.Tests/AutoGIS.Civil3D.Mcp.Tests.csproj -c Release --filter FullyQualifiedName~ProposalPreviewProtocolTests` red on the oversize case. Add the byte guard inside Task 5's `FormatResult`, after serialization and before its return. Keep the complete four-field action projection, the validated `plan.FinalRootComponents[1]` leaf, and `ExistingGroundState.Pending`; never call `ProposalPlan.ToJson()`.

```csharp
if (JsonSerializer.SerializeToUtf8Bytes(body).Length > 64 * 1024)
    return Error("PREVIEW_TOO_LARGE");
```

- [ ] Return the complete structured result only after the size check passes. Run focused tests green and commit. Do not add pagination, truncation, or count-only fallback.

### Task 7: Document local operation and verify the whole change

**Files:** `docs/mcp-handoff-local.md`, all files changed in Tasks 3–6.

**Interfaces:** Consumes the working two-tool stdio server from Tasks 3–6. Produces operator guidance, full managed verification evidence, and the reviewed M2 source PR.

- [ ] Update the local MCP note for two tools, `AUTOGIS_MCP_STANDARDS_MANIFEST`, the startup snapshot/restart rule, the four manifest errors, and the advisory/native qualification boundary. Include the second tool's input/output examples with synthetic names and no owner paths.
- [ ] Run the repository's hosted-equivalent checks:

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode -p:Civil3DYear=2026
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore -p:Civil3DYear=2026
dotnet test AutoGIS.Civil3D.sln -c Release --no-build
dotnet format AutoGIS.Civil3D.sln --verify-no-changes
python tools/checks/docs_checks.py --root . --baseline origin/main
git diff --check
```

- [ ] Run a built child-process initialize/list/call trace on the synthetic fixture. Capture sanitized evidence of the two tool names, schema, one `AdvisoryPlan`, one `NoPlan`, and an M1 validation; verify stdout contains only JSON-RPC messages.
- [ ] Review the final diff against each design field and Review Focus test. Obtain one cold independent adversarial review, fix substantiated findings, rerun affected checks, push the exact head, and wait for hosted checks to pass.
- [ ] Before merging, refetch exact PR head/base/checks and use expected-head protected merge. Verify the merge commit's parent and tree match the reviewed PR head, then report M2 as managed advisory planning only. Do not close native issue #105 or claim Phase 4 acceptance.

## Plan self-check

- The gate, SDK dispatch proof, startup snapshot, argument rules, planner result, projection limit, M1 regression, and final delivery each have a named task and test.
- All implementation paths, commands, error codes, and tool fields are specified. The only runtime-selected value is the owner-provided manifest path; no source step depends on real templates or Civil 3D.
- Tasks 3–6 are test first, minimal code, focused green check, commit. A failing SDK dispatch spike is resolved at Task 3 before the remaining behavior is built.
