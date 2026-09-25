using AutoGIS.Civil3D.Mcp;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
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
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, ct) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request &&
            request.Method == "tools/call" &&
            request.Params is JsonObject parameters &&
            parameters["name"]?.GetValue<string>() == "preview_proposal" &&
            parameters["arguments"] is JsonObject arguments)
        {
            try
            {
                _ = arguments.ToJsonString();
            }
            catch (InvalidOperationException)
            {
                parameters["arguments"] = new JsonObject();
            }
        }
        await next(context, ct);
    }))
    .WithTools(new HandoffTools(Environment.GetEnvironmentVariable("AUTOGIS_MCP_BUNDLE_ROOT")))
    .WithTools([tool]);

await builder.Build().RunAsync();
