using System.Collections.Generic;
using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Orchestrates the full migration pipeline:
/// Roslyn parser -> source IR -> adapter.Adapt() -> target IR -> renderer -> ReportBuilder -> MigrationReport.
/// Adapter is optional; when absent, all targets remain unresolved.
/// </summary>
public class MigrationPipeline
{
    readonly ITestFileParser _parser;
    readonly IRenderer _renderer;
    readonly IProjectAdapter? _adapter;

    public MigrationPipeline(ITestFileParser parser, IRenderer renderer, IProjectAdapter? adapter = null)
    {
        _parser = parser;
        _renderer = renderer;
        _adapter = adapter;
    }

    /// <summary>
    /// Run the full pipeline for a single source file.
    /// Returns the generated C# output and a structured report.
    /// </summary>
    public PipelineResult ProcessFile(string filePath)
    {
        var sourceModel = _parser.Parse(filePath);
        var targetModel = _adapter != null ? _adapter.Adapt(sourceModel) : sourceModel;
        var generated = _renderer.Render(targetModel);
        var report = ReportBuilder.Build(targetModel, generated);

        return new PipelineResult(sourceModel, targetModel, generated, report);
    }

    /// <summary>
    /// Run the full pipeline for all matching files in a directory.
    /// </summary>
    public IEnumerable<PipelineResult> ProcessDirectory(string directoryPath)
    {
        var models = _parser.ParseDirectory(directoryPath);
        var results = new List<PipelineResult>();
        foreach (var sourceModel in models)
        {
            var targetModel = _adapter != null ? _adapter.Adapt(sourceModel) : sourceModel;
            var generated = _renderer.Render(targetModel);
            var report = ReportBuilder.Build(targetModel, generated);
            results.Add(new PipelineResult(sourceModel, targetModel, generated, report));
        }
        return results;
    }
}

/// <summary>
/// Carries the result of processing a single source file through the migration pipeline.
/// </summary>
public record PipelineResult(
    TestFileModel SourceModel,
    TestFileModel TargetModel,
    string GeneratedOutput,
    MigrationReport Report
);
