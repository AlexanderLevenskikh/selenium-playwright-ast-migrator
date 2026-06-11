using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Orchestrates the full migration pipeline:
/// Roslyn parser -> source IR -> renderer (+ adapter) -> generated C# -> MigrationReport.
/// Adapter is optional; when absent, all targets remain unmapped.
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
        var model = _parser.Parse(filePath);
        var generated = _renderer.Render(model, _adapter);

        var report = BuildReport(model, generated);

        return new PipelineResult(model, generated, report);
    }

    /// <summary>
    /// Run the full pipeline for all matching files in a directory.
    /// </summary>
    public IEnumerable<PipelineResult> ProcessDirectory(string directoryPath)
    {
        var models = _parser.ParseDirectory(directoryPath);
        return models.Select(model =>
        {
            var generated = _renderer.Render(model, _adapter);
            var report = BuildReport(model, generated);
            return new PipelineResult(model, generated, report);
        }).ToList();
    }

    MigrationReport BuildReport(TestFileModel model, string generatedOutput)
    {
        var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();
        var allSetupActions = model.SetUpActions.ToList();
        var allFileActions = allActions.Concat(allSetupActions).ToList();

        var unsupportedActions = allFileActions.OfType<UnsupportedAction>().ToList();
        var semanticCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.Semantic);
        var syntaxFallbackCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.SyntaxFallback);

        var uiActions = allFileActions.OfType<ClickAction>().Cast<object>()
            .Concat(allFileActions.OfType<SendKeysAction>()).ToList();

        var mappedTargets = _adapter is not null
            ? allFileActions.Count(a =>
            {
                if (a is ClickAction click)
                    return _adapter.ResolveTarget(click.TargetExpression).IsMapped;
                if (a is SendKeysAction sk)
                    return _adapter.ResolveTarget(sk.TargetExpression).IsMapped;
                return false;
            })
            : 0;

        var unmappedTargets = _adapter is not null
            ? allFileActions.Count(a =>
            {
                if (a is ClickAction click)
                    return !_adapter.ResolveTarget(click.TargetExpression).IsMapped;
                if (a is SendKeysAction sk)
                    return !_adapter.ResolveTarget(sk.TargetExpression).IsMapped;
                return false;
            })
            : uiActions.Count;

        var todoComments = generatedOutput.Split('\n').Count(line =>
            line.TrimStart().StartsWith("// TODO:"));

        return new MigrationReport(
            SourceFilePath: model.FilePath,
            TotalTests: model.Tests.Count(),
            SuccessfullyConvertedTests: model.Tests.Count(t => !t.BodyActions.Any(a => a is UnsupportedAction)),
            UnsupportedActions: unsupportedActions,
            GeneratedOutput: generatedOutput,
            SemanticActions: semanticCount,
            SyntaxFallbackActions: syntaxFallbackCount,
            UnsupportedCount: unsupportedActions.Count,
            MappedTargets: mappedTargets,
            UnmappedTargets: unmappedTargets,
            TodoComments: todoComments
        );
    }
}

/// <summary>
/// Carries the result of processing a single source file through the migration pipeline.
/// </summary>
public record PipelineResult(
    TestFileModel SourceModel,
    string GeneratedOutput,
    MigrationReport Report
);
