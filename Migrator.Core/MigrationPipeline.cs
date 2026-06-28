using System.Collections.Generic;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;

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
    readonly ITargetBackend? _targetBackend;
    readonly MigrationPipelineRenderMode _renderMode;
    readonly SourceSpec? _sourceSpec;

    public MigrationPipeline(ITestFileParser parser, IRenderer renderer, IProjectAdapter? adapter = null, SourceSpec? sourceSpec = null)
    {
        _parser = parser;
        _renderer = renderer;
        _adapter = adapter;
        _targetBackend = null;
        _renderMode = MigrationPipelineRenderMode.Legacy;
        _sourceSpec = sourceSpec;
    }

    public MigrationPipeline(ITestFileParser parser, ITargetBackend targetBackend, IProjectAdapter? adapter = null, MigrationPipelineRenderMode renderMode = MigrationPipelineRenderMode.Legacy, SourceSpec? sourceSpec = null)
    {
        _parser = parser;
        _targetBackend = targetBackend ?? throw new ArgumentNullException(nameof(targetBackend));
        _renderer = new TargetBackendRendererAdapter(_targetBackend);
        _adapter = adapter;
        _renderMode = renderMode;
        _sourceSpec = sourceSpec;
    }

    /// <summary>
    /// Run the full pipeline for a single source file.
    /// Returns the generated C# output and a structured report.
    /// </summary>
    public PipelineResult ProcessFile(string filePath)
    {
        var sourceModel = _parser.Parse(filePath);
        var targetModel = _adapter != null ? _adapter.Adapt(sourceModel) : sourceModel;
        var generated = RenderTargetModel(targetModel);
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
            var generated = RenderTargetModel(targetModel);
            var report = ReportBuilder.Build(targetModel, generated);
            results.Add(new PipelineResult(sourceModel, targetModel, generated, report));
        }
        return results;
    }

    string RenderTargetModel(TestFileModel targetModel)
    {
        if (_renderMode == MigrationPipelineRenderMode.IrV2)
        {
            if (_targetBackend == null)
                throw new InvalidOperationException("IR V2 rendering requires a target backend.");

            var document = LegacyIrBridge.ToDocument(targetModel, source: _sourceSpec, target: _targetBackend.Target);
            return _targetBackend.RenderDocument(document);
        }

        return _renderer.Render(targetModel);
    }

}

/// <summary>
/// Controls whether the migration pipeline renders from the legacy model or from the
/// experimental IR V2 compatibility path. Legacy remains the production default.
/// </summary>
public enum MigrationPipelineRenderMode
{
    Legacy,
    IrV2
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
