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
        await using McpClient client = await ConnectAsync();
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
        await using McpClient client = await ConnectAsync();
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

        CallToolResult valid = await client.CallToolAsync("preview_proposal", ValidArguments);
        Assert.Equal(true, valid.IsError);
        Assert.Null(valid.StructuredContent);
        Assert.Equal("MANIFEST_NOT_CONFIGURED",
            Assert.Single(valid.Content.OfType<TextContentBlock>()).Text);
    }

    private static Dictionary<string, object?> ValidArguments => new()
    {
        ["client_name"] = "Client",
        ["site_name"] = "Site",
        ["proposal_year"] = 2026,
        ["orientation"] = "Landscape",
        ["sheet_size"] = "TEST-A1"
    };

    private static async Task<McpClient> ConnectAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "AutoGIS M2 protocol test",
            Command = "dotnet",
            Arguments = ["exec", ServerDllPath],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["AUTOGIS_MCP_STANDARDS_MANIFEST"] = null,
                ["AUTOGIS_MCP_BUNDLE_ROOT"] = null
            }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    }

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
