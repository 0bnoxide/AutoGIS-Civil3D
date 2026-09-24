using AutoGIS.Civil3D.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools(new HandoffTools(Environment.GetEnvironmentVariable("AUTOGIS_MCP_BUNDLE_ROOT")));

await builder.Build().RunAsync();
