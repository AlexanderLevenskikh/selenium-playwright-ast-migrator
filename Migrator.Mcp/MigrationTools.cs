using System.ComponentModel;
using System.Text.Json;
using Migrator.Application;
using ModelContextProtocol.Server;

namespace Migrator.Mcp;

[McpServerToolType]
public static class MigrationTools
{
    [McpServerTool(Name = "analyze_migration")]
    [Description("Analyze Selenium C# source with an independent inventory. Unsupported xUnit and multiple fixtures are counted and reported, not silently converted. Does not change files.")]
    public static string AnalyzeMigration(MigrationEngine engine, WorkspacePaths workspace,
        [Description("Source .cs file or directory relative to the configured workspace root")] string source,
        [Description("Optional JSON adapter config path within workspace")] string? config = null,
        CancellationToken cancellationToken = default)
    {
        var result = engine.Analyze(workspace.Resolve(source), config == null ? null : workspace.Resolve(config),
            cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "preview_migration")]
    [Description("Generate a read-only conservative Playwright .NET candidate; this is NOT a syntax, compile, runtime or behavior verification. Unsupported source shapes are blocked.")]
    public static string PreviewMigration(MigrationEngine engine, WorkspacePaths workspace,
        [Description("Source .cs file or directory relative to the workspace")] string source,
        [Description("Optional JSON adapter config path within workspace")] string? config = null,
        [Description("Expected analysisId from analyze_migration. Stale input is rejected.")] string? expectedAnalysisId = null,
        CancellationToken cancellationToken = default)
    {
        var result = engine.Preview(workspace.Resolve(source),
            config == null ? null : workspace.Resolve(config), expectedAnalysisId, cancellationToken);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        if (json.Length > 1_000_000)
            throw new InvalidOperationException("PREVIEW_TOO_LARGE: narrow the source scope. No files were changed.");
        return json;
    }

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
