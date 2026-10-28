using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Migrator.Application;
using Migrator.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders(); // stdout is reserved for MCP JSON-RPC
builder.Services.AddSingleton<MigrationEngine>();
builder.Services.AddSingleton<WorkspacePaths>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
