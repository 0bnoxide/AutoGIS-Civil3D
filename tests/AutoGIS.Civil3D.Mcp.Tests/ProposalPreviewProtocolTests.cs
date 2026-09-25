using System.Text.Json;
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

    private static Dictionary<string, object?> ValidArguments => new()
    {
        ["client_name"] = "Client",
        ["site_name"] = "Site",
        ["proposal_year"] = 2026,
        ["orientation"] = "Landscape",
        ["sheet_size"] = "TEST-A1"
    };

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
