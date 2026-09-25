using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoGIS.Civil3D.Proposal;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace AutoGIS.Civil3D.Mcp.Tests;

public sealed class ProposalPreviewProtocolTests
{
    [Fact]
    public async Task Stdio_lists_preview_with_explicit_schemas()
    {
        await using McpClient client = await ConnectAsync(ManifestFixturePath);
        var tools = await client.ListToolsAsync();
        Assert.Equal(new[] { "preview_proposal", "validate_handoff_bundle" },
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray());

        var preview = Assert.Single(tools, tool => tool.Name == "preview_proposal");
        JsonElement input = preview.ProtocolTool.InputSchema;
        Assert.Equal("object", input.GetProperty("type").GetString());
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        JsonElement properties = input.GetProperty("properties");
        Assert.Equal(10, properties.EnumerateObject().Count());
        Assert.Equal(new[] { "client_name", "site_name", "proposal_year", "orientation", "sheet_size" },
            input.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray());
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            Assert.Equal(property.Name == "proposal_year" ? "integer" : "string",
                property.Value.GetProperty("type").GetString());
        }
        Assert.True(preview.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.NotNull(preview.ProtocolTool.OutputSchema);
    }

    [Fact]
    public async Task Stdio_rejects_invalid_argument_shapes_with_one_safe_code()
    {
        await using McpClient client = await ConnectAsync(ManifestFixturePath);
        var wrongType = ValidArguments;
        wrongType["proposal_year"] = "2026";
        var extra = ValidArguments;
        extra["unexpected"] = true;
        var optionalNull = ValidArguments;
        optionalNull["project_manager"] = null;
        var overlong = ValidArguments;
        overlong["client_name"] = new string('x', 1025);
        var cases = new (string Name, Dictionary<string, object?> Arguments)[]
        {
            ("missing required", new() { ["client_name"] = "Client" }),
            ("wrong type", wrongType),
            ("unknown key", extra),
            ("optional null", optionalNull),
            ("overlong string", overlong)
        };

        foreach (var (name, arguments) in cases)
        {
            CallToolResult result = await client.CallToolAsync("preview_proposal", arguments);
            Assert.True(result.IsError == true, name);
            Assert.Null(result.StructuredContent);
            Assert.Equal("INVALID_ARGUMENTS",
                Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        }

    }

    [Fact]
    public async Task Stdio_rejects_lone_surrogate_escapes_as_invalid_arguments()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(ServerDllPath);
        start.Environment["AUTOGIS_MCP_STANDARDS_MANIFEST"] = ManifestFixturePath;
        start.Environment["AUTOGIS_MCP_BUNDLE_ROOT"] = FixtureRoot;
        using Process process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        async Task<JsonElement> Exchange(string request)
        {
            await process.StandardInput.WriteLineAsync(request);
            string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.NotNull(line);
            using JsonDocument response = JsonDocument.Parse(line);
            return response.RootElement.Clone();
        }

        try
        {
            JsonElement initialized = await Exchange("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"regression","version":"1"}}}""");
            Assert.True(initialized.TryGetProperty("result", out _), initialized.GetRawText());
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

            string[] requests =
            [
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"preview_proposal","arguments":{"client_name":"\ud800","site_name":"Site","proposal_year":2026,"orientation":"Landscape","sheet_size":"TEST-A1"}}}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"preview_proposal","arguments":{"client_name":"Client","site_name":"Site","proposal_year":2026,"orientation":"Landscape","sheet_size":"TEST-A1","site_address":"\udfff"}}}"""
            ];
            foreach (string request in requests)
            {
                JsonElement response = await Exchange(request);
                Assert.True(response.TryGetProperty("result", out JsonElement result),
                    response.GetRawText());
                Assert.True(result.GetProperty("isError").GetBoolean());
                Assert.Equal("INVALID_ARGUMENTS",
                    result.GetProperty("content")[0].GetProperty("text").GetString());
                Assert.True(!result.TryGetProperty("structuredContent", out JsonElement body) ||
                    body.ValueKind == JsonValueKind.Null);
            }

            JsonElement valid = await Exchange("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"preview_proposal","arguments":{"client_name":"Client","site_name":"Site","proposal_year":2026,"orientation":"Landscape","sheet_size":"TEST-A1"}}}""");
            Assert.Equal("AdvisoryPlan", valid.GetProperty("result")
                .GetProperty("structuredContent").GetProperty("status").GetString());

            JsonElement m1 = await Exchange("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"validate_handoff_bundle","arguments":{"bundle_relative_path":"valid/known-vertical-datum.zip"}}}""");
            JsonElement m1Result = m1.GetProperty("result");
            Assert.True(!m1Result.TryGetProperty("isError", out JsonElement m1Error) ||
                !m1Error.GetBoolean());
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task Stdio_returns_safe_planner_rejections()
    {
        await using McpClient client = await ConnectAsync(ManifestFixturePath);
        var cases = new (string Name, string Key, object Value, string Code, string Explanation)[]
        {
            ("blank optional", "project_manager", " ", "PROPOSAL_INVALID_INPUTS", "Check proposal input values."),
            ("unsafe name", "client_name", "Client/Other", "PROPOSAL_INVALID_INPUTS", "Check proposal input values."),
            ("unsupported year", "proposal_year", 2030, "PROPOSAL_UNSUPPORTED_YEAR", "Choose a year in configured standards."),
            ("out-of-range year", "proposal_year", 10000, "PROPOSAL_INVALID_INPUTS", "Check proposal input values."),
            ("unsupported orientation", "orientation", "Portrait", "PROPOSAL_UNSUPPORTED_SHEET", "Choose a configured sheet profile."),
            ("unsupported size", "sheet_size", "TEST-A0", "PROPOSAL_UNSUPPORTED_SHEET", "Choose a configured sheet profile.")
        };

        foreach (var (name, key, value, code, explanation) in cases)
        {
            var arguments = ValidArguments;
            arguments[key] = value;
            CallToolResult result = await client.CallToolAsync("preview_proposal", arguments);
            Assert.NotEqual(true, result.IsError);
            JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
            Assert.Equal("NoPlan", body.GetProperty("status").GetString());
            Assert.True(body.GetProperty("advisoryOnly").GetBoolean());
            Assert.False(body.GetProperty("nativePreflightPerformed").GetBoolean());
            Assert.False(body.GetProperty("templatesChecked").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("manifestVersion").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("proposedRootName").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("existingGround").ValueKind);
            Assert.Equal(0, body.GetProperty("actionCount").GetInt32());
            Assert.Empty(body.GetProperty("actions").EnumerateArray());
            JsonElement issue = Assert.Single(body.GetProperty("issues").EnumerateArray());
            Assert.Equal(code, issue.GetProperty("code").GetString());
            Assert.Equal(explanation, issue.GetProperty("explanation").GetString());
            Assert.Equal(2, issue.EnumerateObject().Count());
        }
    }

    [Fact]
    public async Task Stdio_returns_complete_advisory_plan_from_synthetic_standards()
    {
        await using McpClient client = await ConnectAsync(ManifestFixturePath);
        CallToolResult result = await client.CallToolAsync("preview_proposal", ValidArguments);

        Assert.NotEqual(true, result.IsError);
        JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(10, body.EnumerateObject().Count());
        Assert.Equal("AdvisoryPlan", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("advisoryOnly").GetBoolean());
        Assert.False(body.GetProperty("nativePreflightPerformed").GetBoolean());
        Assert.False(body.GetProperty("templatesChecked").GetBoolean());
        Assert.Equal(1, body.GetProperty("manifestVersion").GetInt32());
        Assert.Equal("Client - Site", body.GetProperty("proposedRootName").GetString());
        Assert.Equal("Pending", body.GetProperty("existingGround").GetString());
        Assert.Equal(36, body.GetProperty("actionCount").GetInt32());
        JsonElement[] actions = body.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Equal(36, actions.Length);
        Assert.Empty(body.GetProperty("issues").EnumerateArray());
        Assert.Equal("00:root", actions[0].GetProperty("id").GetString());
        Assert.Equal("CreateFolder", actions[0].GetProperty("operation").GetString());
        Assert.Equal("", actions[0].GetProperty("relativePath").GetString());
        Assert.Empty(actions[0].GetProperty("dependencies").EnumerateArray());
        Assert.All(actions, action => Assert.Equal(4, action.EnumerateObject().Count()));
        Assert.DoesNotContain("C:/AutoGIS-Synthetic", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stdio_projects_every_planner_action_in_order()
    {
        PlanResult expected = ProposalPlanner.Build(
            new ProposalInputs("Client", "Site", 2026, "Landscape", "TEST-A1"),
            StandardsManifest.Parse(File.ReadAllBytes(ManifestFixturePath)).Manifest!);
        await using McpClient client = await ConnectAsync(ManifestFixturePath);
        CallToolResult reply = await client.CallToolAsync("preview_proposal", ValidArguments);
        JsonElement actual = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(actual).Length, 1, 64 * 1024);

        Assert.Equal(new[] { "actionCount", "actions", "advisoryOnly", "existingGround",
            "issues", "manifestVersion", "nativePreflightPerformed", "proposedRootName",
            "status", "templatesChecked" }, actual.EnumerateObject().Select(p => p.Name)
                .Order(StringComparer.Ordinal).ToArray());
        Assert.True(actual.GetProperty("advisoryOnly").GetBoolean());
        Assert.False(actual.GetProperty("nativePreflightPerformed").GetBoolean());
        Assert.False(actual.GetProperty("templatesChecked").GetBoolean());
        JsonElement actions = actual.GetProperty("actions");
        Assert.Equal(expected.Plan!.Actions.Length, actions.GetArrayLength());
        Assert.Equal(actions.GetArrayLength(), actual.GetProperty("actionCount").GetInt32());
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
            Assert.Equal(action.Dependencies.ToArray(),
                actions[i].GetProperty("dependencies").EnumerateArray()
                    .Select(item => item.GetString()).ToArray());
        }
    }

    [Fact]
    public async Task Stdio_keeps_startup_manifest_snapshot_until_restart()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("AutoGIS-M2-Snapshot-");
        try
        {
            string path = Path.Combine(temp.FullName, "snapshot.json");
            File.Copy(ManifestFixturePath, path);
            string[] fixtureInventory = Inventory(FixtureRoot);
            await using (McpClient running = await ConnectAsync(path, FixtureRoot))
            {
                byte[] firstBytes = File.ReadAllBytes(path);
                string[] firstInventory = Inventory(temp.FullName);
                CallToolResult first = await running.CallToolAsync("preview_proposal", ValidArguments);
                Assert.NotEqual(true, first.IsError);
                Assert.Equal(firstBytes, File.ReadAllBytes(path));
                Assert.Equal(firstInventory, Inventory(temp.FullName));
                Assert.Equal(fixtureInventory, Inventory(FixtureRoot));

                File.WriteAllText(path, "{");
                byte[] secondBytes = File.ReadAllBytes(path);
                string[] secondInventory = Inventory(temp.FullName);
                CallToolResult second = await running.CallToolAsync("preview_proposal", ValidArguments);
                Assert.Equal(first.StructuredContent?.GetRawText(),
                    second.StructuredContent?.GetRawText());
                Assert.Equal(secondBytes, File.ReadAllBytes(path));
                Assert.Equal(secondInventory, Inventory(temp.FullName));
                Assert.Equal(fixtureInventory, Inventory(FixtureRoot));
            }

            byte[] restartBytes = File.ReadAllBytes(path);
            string[] restartInventory = Inventory(temp.FullName);
            await using McpClient restarted = await ConnectAsync(path, FixtureRoot);
            CallToolResult invalid = await restarted.CallToolAsync("preview_proposal", ValidArguments);
            Assert.True(invalid.IsError == true);
            Assert.Null(invalid.StructuredContent);
            Assert.Equal("MANIFEST_INVALID",
                Assert.Single(invalid.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(restartBytes, File.ReadAllBytes(path));
            Assert.Equal(restartInventory, Inventory(temp.FullName));
            Assert.Equal(fixtureInventory, Inventory(FixtureRoot));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Stdio_redacts_manifest_paths_templates_and_optional_metadata()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("AutoGIS-M2-Redaction-");
        try
        {
            string path = Path.Combine(temp.FullName, "redaction.json");
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(ManifestFixturePath))!;
            manifest["baseRoots"]![0]!["path"] = "C:/HOST_ROOT_MARKER/2026";
            manifest["modelTemplate"] = "C:/TEMPLATE_MARKER/Model.dwt";
            manifest["profiles"]![0]!["template"] = "C:/TEMPLATE_MARKER/Sheet.dwt";
            File.WriteAllText(path, manifest.ToJsonString());
            byte[] before = File.ReadAllBytes(path);
            string[] inventory = Inventory(temp.FullName);
            string[] fixtureInventory = Inventory(FixtureRoot);
            var arguments = ValidArguments;
            arguments["project_manager"] = "OPTIONAL_METADATA_MARKER";
            arguments["site_address"] = "OPTIONAL_METADATA_MARKER";
            await using McpClient client = await ConnectAsync(path, FixtureRoot);
            CallToolResult reply = await client.CallToolAsync("preview_proposal", arguments);
            JsonElement body = Assert.IsType<JsonElement>(reply.StructuredContent);
            string raw = body.GetRawText();

            Assert.DoesNotContain("HOST_ROOT_MARKER", raw);
            Assert.DoesNotContain("TEMPLATE_MARKER", raw);
            Assert.DoesNotContain("OPTIONAL_METADATA_MARKER", raw);
            Assert.DoesNotContain("finalRoot", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"configuration\":", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"data\"", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("approvalToken", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("planDigest", raw, StringComparison.OrdinalIgnoreCase);
            var rejectedArguments = ValidArguments;
            rejectedArguments["proposal_year"] = 2030;
            CallToolResult rejected = await client.CallToolAsync("preview_proposal", rejectedArguments);
            string rejectedRaw = Assert.IsType<JsonElement>(rejected.StructuredContent).GetRawText();
            Assert.DoesNotContain("The manifest has no root for the selected proposal year.", rejectedRaw);
            Assert.DoesNotContain("ProposalYear", rejectedRaw);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(inventory, Inventory(temp.FullName));
            Assert.Equal(fixtureInventory, Inventory(FixtureRoot));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Stdio_rejects_complete_projection_over_64_kib_without_partial_graph()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("AutoGIS-M2-Large-");
        try
        {
            string path = Path.Combine(temp.FullName, "large.json");
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(ManifestFixturePath))!;
            JsonArray folders = manifest["folders"]!.AsArray();
            for (int i = 0; i < 600; i++)
                folders.Add($"X{i:D4}{new string('a', 100)}");
            File.WriteAllText(path, manifest.ToJsonString());
            Assert.InRange(new FileInfo(path).Length, 1, 256 * 1024);
            Assert.NotNull(StandardsManifest.Parse(File.ReadAllBytes(path)).Manifest);
            byte[] before = File.ReadAllBytes(path);
            string[] inventory = Inventory(temp.FullName);
            string[] fixtureInventory = Inventory(FixtureRoot);
            await using McpClient client = await ConnectAsync(path, FixtureRoot);
            CallToolResult tooLarge = await client.CallToolAsync("preview_proposal", ValidArguments);

            Assert.True(tooLarge.IsError == true);
            Assert.Null(tooLarge.StructuredContent);
            Assert.Equal("PREVIEW_TOO_LARGE",
                Assert.Single(tooLarge.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(inventory, Inventory(temp.FullName));
            Assert.Equal(fixtureInventory, Inventory(FixtureRoot));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Startup_manifest_configuration_returns_only_safe_codes_and_keeps_m1_available()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("AutoGIS-Civil3D-M2Manifest-");
        try
        {
            string missing = Path.Combine(temp.FullName, "missing.json");
            string unreadable = Path.Combine(temp.FullName, "directory.json");
            Directory.CreateDirectory(unreadable);
            string tooLarge = Path.Combine(temp.FullName, "too-large.json");
            await File.WriteAllBytesAsync(tooLarge, new byte[256 * 1024 + 1]);
            string badUtf8 = Path.Combine(temp.FullName, "bad-utf8.json");
            await File.WriteAllBytesAsync(badUtf8, [0xC3, 0x28]);
            string badJson = Path.Combine(temp.FullName, "bad-json.json");
            await File.WriteAllTextAsync(badJson, "{");
            string rejected = Path.Combine(temp.FullName, "rejected.json");
            await File.WriteAllTextAsync(rejected, "{\"version\":1}");

            var cases = new (string Name, string? Path, string Code)[]
            {
                ("absent", null, "MANIFEST_NOT_CONFIGURED"),
                ("blank", " ", "MANIFEST_NOT_CONFIGURED"),
                ("relative", "standards.json", "MANIFEST_NOT_CONFIGURED"),
                ("wrong extension", Path.Combine(temp.FullName, "standards.txt"), "MANIFEST_NOT_CONFIGURED"),
                ("UNC", @"\\server\share\standards.json", "MANIFEST_NOT_CONFIGURED"),
                ("device", @"\\?\C:\standards.json", "MANIFEST_NOT_CONFIGURED"),
                ("slash UNC", "//server/share/standards.json", "MANIFEST_NOT_CONFIGURED"),
                ("missing", missing, "MANIFEST_UNREADABLE"),
                ("unreadable", unreadable, "MANIFEST_UNREADABLE"),
                ("too large", tooLarge, "MANIFEST_TOO_LARGE"),
                ("bad UTF-8", badUtf8, "MANIFEST_INVALID"),
                ("bad JSON", badJson, "MANIFEST_INVALID"),
                ("parser rejection", rejected, "MANIFEST_INVALID")
            };

            foreach (var (name, path, code) in cases)
            {
                await using McpClient client = await ConnectAsync(path, FixtureRoot);
                Assert.Equal(2, (await client.ListToolsAsync()).Count);
                CallToolResult error = await client.CallToolAsync("preview_proposal", ValidArguments);
                Assert.True(error.IsError == true, name);
                Assert.Null(error.StructuredContent);
                Assert.Equal(code, Assert.Single(error.Content.OfType<TextContentBlock>()).Text);

                CallToolResult m1 = await client.CallToolAsync("validate_handoff_bundle",
                    new Dictionary<string, object?>
                    {
                        ["bundle_relative_path"] = "valid/known-vertical-datum.zip"
                    });
                Assert.NotEqual(true, m1.IsError);
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(@"\/server/share/standards.json")]
    [InlineData(@"/\server/share/standards.json")]
    public void Manifest_path_guard_rejects_mixed_separator_unc_without_io(string path)
    {
        var guard = typeof(ProposalTools).GetMethod("IsLocalJsonPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(guard);
        Assert.False(Assert.IsType<bool>(guard.Invoke(null, [path])));
    }

    private static Dictionary<string, object?> ValidArguments => new()
    {
        ["client_name"] = "Client",
        ["site_name"] = "Site",
        ["proposal_year"] = 2026,
        ["orientation"] = "Landscape",
        ["sheet_size"] = "TEST-A1"
    };

    private static string[] Inventory(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal).ToArray();

    private static async Task<McpClient> ConnectAsync(string? manifestPath = null,
        string? bundleRoot = null)
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

    private static string FixtureRoot => Path.Combine(RepositoryRoot, "fixtures", "v1");

    private static string ManifestFixturePath => Path.Combine(RepositoryRoot, "tests",
        "AutoGIS.Civil3D.Proposal.Tests", "Fixtures", "synthetic-standards.json");

    private static string ServerDllPath => Path.Combine(RepositoryRoot, "src",
        "AutoGIS.Civil3D.Mcp", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net8.0",
        "AutoGIS.Civil3D.Mcp.dll");

    private static string RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
                dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "tests",
                    "AutoGIS.Civil3D.Proposal.Tests", "Fixtures",
                    "synthetic-standards.json")))
                    return dir.FullName;
            }
            throw new DirectoryNotFoundException("Proposal test fixture not found.");
        }
    }
}
