using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Migrator.Application;

var builder = Host.CreateApplicationBuilder(args);
// Stdout belongs exclusively to MCP JSON-RPC transport.
builder.Logging.ClearProviders();
builder.Services.AddSingleton<MigrationEngine>();
builder.Services.AddSingleton<WorkspacePaths>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
