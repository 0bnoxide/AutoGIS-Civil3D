using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AutoGIS.Civil3D.Handoff.Validation;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace AutoGIS.Civil3D.Mcp.Tests;

public sealed class McpProtocolTests
{
    private const string ToolName = "validate_handoff_bundle";

    [Fact]
    public async Task Stdio_client_lists_one_tool_and_preserves_package_outcomes()
    {
        await using McpClient client = await ConnectAsync(FixtureRoot);
        var tool = Assert.Single(await client.ListToolsAsync()).ProtocolTool;
        Assert.Equal(ToolName, tool.Name);

        JsonElement input = tool.InputSchema;
        Assert.Equal("object", input.GetProperty("type").GetString());
        Assert.Equal("string", input.GetProperty("properties")
            .GetProperty("bundle_relative_path").GetProperty("type").GetString());
        Assert.Contains("bundle_relative_path", input.GetProperty("required")
            .EnumerateArray().Select(item => item.GetString()));
        Assert.NotNull(tool.OutputSchema);
        JsonElement outputProperties = tool.OutputSchema.Value.GetProperty("properties");
        foreach (string field in new[] { "status", "issueCount", "truncated", "issues", "metadata" })
        {
            Assert.True(outputProperties.TryGetProperty(field, out _), $"Missing output field: {field}");
        }

        foreach (var (path, status, primaryCode) in new[]
        {
            ("valid/known-vertical-datum.zip", "Valid", (string?)null),
            ("valid/unknown-vertical-datum.zip", "ValidWithWarnings", "WRN001"),
            ("invalid/malformed-archive.zip", "Invalid", "ZIP001")
        })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await client.CallToolAsync(ToolName,
                new Dictionary<string, object?> { ["bundle_relative_path"] = path },
                cancellationToken: timeout.Token);
            Assert.NotEqual(true, result.IsError);
            Assert.Empty(result.Content);
            JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
            Assert.Equal(status, body.GetProperty("status").GetString());
            Assert.False(body.GetProperty("truncated").GetBoolean());
            JsonElement issues = body.GetProperty("issues");
            Assert.Equal(body.GetProperty("issueCount").GetInt32(), issues.GetArrayLength());
            Assert.Equal(primaryCode, issues.GetArrayLength() == 0
                ? null : issues[0].GetProperty("code").GetString());
            Assert.Equal(status == "Invalid", body.GetProperty("metadata").ValueKind == JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Validation_does_not_change_staged_files_and_works_after_restart()
    {
        DirectoryInfo stage = CreateStaging();
        try
        {
            var before = Snapshot(stage.FullName);
            for (int start = 0; start < 2; start++)
            {
                await using (McpClient client = await ConnectAsync(stage.FullName))
                {
                    var result = await CallAsync(client, "valid/known-vertical-datum.zip");
                    Assert.NotEqual(true, result.IsError);
                    Assert.Equal("Valid", result.StructuredContent!.Value.GetProperty("status").GetString());
                }

                Assert.Equal(before, Snapshot(stage.FullName));
            }
        }
        finally
        {
            stage.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Missing_root_missing_file_and_disallowed_paths_have_only_safe_codes()
    {
        await using (McpClient noRoot = await ConnectAsync(null))
        {
            await AssertErrorAsync(noRoot, "valid/known-vertical-datum.zip", "ROOT_NOT_CONFIGURED");
        }

        DirectoryInfo parent = Directory.CreateTempSubdirectory("AutoGIS-Civil3D-McpPaths-");
        try
        {
            string root = Path.Combine(parent.FullName, "stage");
            string sibling = Path.Combine(parent.FullName, "stage-sibling");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(sibling);
            File.Copy(Path.Combine(FixtureRoot, "valid", "known-vertical-datum.zip"),
                Path.Combine(sibling, "outside.zip"));

            await using McpClient client = await ConnectAsync(root);
            await AssertErrorAsync(client, "missing.zip", "FILE_NOT_FOUND");
            await AssertErrorAsync(client, "../stage-sibling/outside.zip", "PATH_NOT_ALLOWED");
            await AssertErrorAsync(client, "../outside.zip", "PATH_NOT_ALLOWED");
            await AssertErrorAsync(client, Path.Combine(root, "valid.zip"), "PATH_NOT_ALLOWED");
            await AssertErrorAsync(client, "bundle.txt", "PATH_NOT_ALLOWED");
            await AssertErrorAsync(client, "", "INVALID_ARGUMENTS");

            if (OperatingSystem.IsWindows())
            {
                foreach (string path in new[]
                {
                    @"..\stage-sibling\outside.zip",
                    @"C:drive-relative.zip",
                    @"\\server\share\bundle.zip",
                    @"\\?\C:\bundle.zip",
                    @"valid.zip:stream",
                    "CON.zip",
                    "CON .zip",
                    "COM1.zip",
                    "COM\u00b9.zip",
                    "CONIN$.zip"
                })
                {
                    await AssertErrorAsync(client, path, "PATH_NOT_ALLOWED");
                }
            }

            var extra = await client.CallToolAsync(ToolName,
                new Dictionary<string, object?>
                {
                    ["bundle_relative_path"] = "missing.zip",
                    ["unexpected"] = true
                });
            Assert.Equal(true, extra.IsError);
            Assert.Equal("INVALID_ARGUMENTS", Assert.Single(extra.Content.OfType<TextContentBlock>()).Text);
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Windows_reparse_points_on_root_ancestor_and_file_are_rejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DirectoryInfo parent = Directory.CreateTempSubdirectory("AutoGIS-Civil3D-McpLinks-");
        try
        {
            string realRoot = Path.Combine(parent.FullName, "real");
            Directory.CreateDirectory(realRoot);
            string realFile = Path.Combine(realRoot, "package.zip");
            File.Copy(Path.Combine(FixtureRoot, "valid", "known-vertical-datum.zip"), realFile);

            string rootLink = Path.Combine(parent.FullName, "root-link");
            Directory.CreateSymbolicLink(rootLink, realRoot);
            await using (McpClient client = await ConnectAsync(rootLink))
            {
                await AssertErrorAsync(client, "package.zip", "PATH_NOT_ALLOWED");
            }

            string rootParentLink = Path.Combine(parent.FullName, "root-parent-link");
            Directory.CreateSymbolicLink(rootParentLink, realRoot);
            string nestedRoot = Path.Combine(rootParentLink, "nested");
            Directory.CreateDirectory(Path.Combine(realRoot, "nested"));
            await using (McpClient client = await ConnectAsync(nestedRoot))
            {
                await AssertErrorAsync(client, "package.zip", "PATH_NOT_ALLOWED");
            }

            string ancestorLink = Path.Combine(realRoot, "ancestor-link");
            Directory.CreateSymbolicLink(ancestorLink, realRoot);
            string fileLink = Path.Combine(realRoot, "file-link.zip");
            File.CreateSymbolicLink(fileLink, realFile);
            await using McpClient normal = await ConnectAsync(realRoot);
            await AssertErrorAsync(normal, "ancestor-link/package.zip", "PATH_NOT_ALLOWED");
            await AssertErrorAsync(normal, "file-link.zip", "PATH_NOT_ALLOWED");
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Child_stdout_contains_only_json_rpc_messages()
    {
        using var child = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        child.StartInfo.ArgumentList.Add("exec");
        child.StartInfo.ArgumentList.Add(ServerDllPath);
        child.StartInfo.Environment["AUTOGIS_MCP_BUNDLE_ROOT"] = FixtureRoot;
        Assert.True(child.Start());
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<string> stderr = child.StandardError.ReadToEndAsync(timeout.Token);
            await child.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"stdout-test","version":"1"}}}""");
            await child.StandardInput.FlushAsync();
            JsonElement initialized = await ReadResponseAsync(1);
            Assert.True(initialized.TryGetProperty("result", out _));

            await child.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await child.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            await child.StandardInput.FlushAsync();
            JsonElement listed = await ReadResponseAsync(2);
            Assert.Equal(ToolName, Assert.Single(listed.GetProperty("result")
                .GetProperty("tools").EnumerateArray()).GetProperty("name").GetString());

            child.StandardInput.Close();
            Task<string> trailing = child.StandardOutput.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, child.ExitCode);
            foreach (string line in (await trailing).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using JsonDocument message = JsonDocument.Parse(line);
                Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
            }

            _ = await stderr;

            async Task<JsonElement> ReadResponseAsync(int id)
            {
                string line = Assert.IsType<string>(
                    await child.StandardOutput.ReadLineAsync(timeout.Token));
                using JsonDocument message = JsonDocument.Parse(line);
                Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
                Assert.Equal(id, message.RootElement.GetProperty("id").GetInt32());
                return message.RootElement.Clone();
            }
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task Cancelled_waiter_keeps_validation_busy_until_read_finishes()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int validations = 0;
        var report = new ValidationReport(ValidationStatus.Valid, [],
            new VerifiedPackageMetadata(Guid.Empty, "Safe Surface", 3, 1, 26913));
        var tools = new HandoffTools(FixtureRoot, _ =>
        {
            Interlocked.Increment(ref validations);
            entered.TrySetResult(true);
            release.Task.GetAwaiter().GetResult();
            return report;
        });

        using var cancellation = new CancellationTokenSource();
        Task<CallToolResult> first = tools.RunValidationAsync("package.zip", cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertToolError(await tools.RunValidationAsync("package.zip", CancellationToken.None), "BUSY");
            cancellation.Cancel();
            AssertToolError(await first.WaitAsync(TimeSpan.FromSeconds(10)), "CANCELLED");
            AssertToolError(await tools.RunValidationAsync("package.zip", CancellationToken.None), "BUSY");
            Assert.Equal(1, Volatile.Read(ref validations));

            release.TrySetResult(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                CallToolResult next = await tools.RunValidationAsync("package.zip", CancellationToken.None);
                if (next.IsError != true)
                {
                    Assert.Equal("Valid", next.StructuredContent!.Value.GetProperty("status").GetString());
                    break;
                }

                AssertToolError(next, "BUSY");
                await Task.Delay(10, timeout.Token);
            }

            Assert.Equal(2, Volatile.Read(ref validations));
        }
        finally
        {
            release.TrySetResult(true);
        }
    }

    [Fact]
    public void Formatter_bounds_and_redacts_many_hostile_issues()
    {
        const string hostile = @"C:\TOP_SECRET_MARKER\artifact.zip";
        ValidationIssue[] issues = Enumerable.Range(0, 300)
            .Select(_ => new ValidationIssue("WRN001", IssueSeverity.Warning,
                "Raw package message: " + hostile, hostile))
            .ToArray();
        var report = new ValidationReport(ValidationStatus.ValidWithWarnings, issues,
            new VerifiedPackageMetadata(Guid.Empty, hostile, 3, 1, 26913));

        CallToolResult result = HandoffTools.FormatReport(report);

        Assert.NotEqual(true, result.IsError);
        Assert.Empty(result.Content);
        JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("ValidWithWarnings", body.GetProperty("status").GetString());
        Assert.Equal(300, body.GetProperty("issueCount").GetInt32());
        Assert.True(body.GetProperty("truncated").GetBoolean());
        Assert.InRange(body.GetProperty("issues").GetArrayLength(), 1, 299);
        Assert.Equal("[redacted]", body.GetProperty("metadata").GetProperty("surfaceName").GetString());
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(body).Length, 1, 65_536);
        Assert.DoesNotContain("TOP_SECRET_MARKER", body.GetRawText());
        foreach (JsonElement issue in body.GetProperty("issues").EnumerateArray())
        {
            Assert.Equal("WRN001", issue.GetProperty("code").GetString());
            Assert.Equal("Warning", issue.GetProperty("severity").GetString());
            Assert.Null(issue.GetProperty("location").GetString());
            Assert.NotEqual("Raw package message: " + hostile, issue.GetProperty("message").GetString());
        }
    }

    private static void AssertToolError(CallToolResult result, string code)
    {
        Assert.Equal(true, result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Equal(code, Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
    }

    private static async Task AssertErrorAsync(McpClient client, string path, string code)
    {
        AssertToolError(await CallAsync(client, path), code);
    }

    private static async Task<CallToolResult> CallAsync(McpClient client, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await client.CallToolAsync(ToolName,
            new Dictionary<string, object?> { ["bundle_relative_path"] = path },
            cancellationToken: timeout.Token);
    }

    private static DirectoryInfo CreateStaging()
    {
        DirectoryInfo stage = Directory.CreateTempSubdirectory("AutoGIS-Civil3D-McpStage-");
        foreach (string relative in new[]
        {
            "valid/known-vertical-datum.zip",
            "valid/unknown-vertical-datum.zip",
            "invalid/malformed-archive.zip"
        })
        {
            string destination = Path.Combine(stage.FullName, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(FixtureRoot, relative), destination);
        }

        return stage;
    }

    private static (string Name, string Hash)[] Snapshot(string root) =>
        Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => (
                Name: Path.GetRelativePath(root, path),
                Hash: Directory.Exists(path) ? "<directory>" :
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

    private static async Task<McpClient> ConnectAsync(string? root)
    {
        Assert.True(File.Exists(ServerDllPath), $"MCP child build missing: {ServerDllPath}");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "AutoGIS M1 protocol test",
            Command = "dotnet",
            Arguments = ["exec", ServerDllPath],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["AUTOGIS_MCP_BUNDLE_ROOT"] = root
            }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    }

    private static string ServerDllPath => Path.Combine(RepositoryRoot, "src", "AutoGIS.Civil3D.Mcp",
        "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net8.0",
        "AutoGIS.Civil3D.Mcp.dll");

    private static string FixtureRoot => Path.Combine(RepositoryRoot, "fixtures", "v1");

    private static string RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? current = new(AppContext.BaseDirectory);
                current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "fixtures", "v1", "valid",
                    "known-vertical-datum.zip")))
                {
                    return current.FullName;
                }
            }

            throw new DirectoryNotFoundException("Repository fixtures were not found.");
        }
    }
}
