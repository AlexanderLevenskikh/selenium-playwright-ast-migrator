using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabBaselineService
{
    public static LabBaselineSnapshot Create(LabSuiteRunResult run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Baseline label must not be empty.", nameof(label));

        var roots = new[] { run.ArtifactsRoot, run.CorpusRoot };
        return new LabBaselineSnapshot
        {
            Label = label.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceRunStartedAtUtc = run.StartedAtUtc,
            Suite = run.Suite,
            CorpusIdentity = Path.GetFileName(Path.TrimEndingDirectorySeparator(run.CorpusRoot)),
            Projects = run.Projects
                .OrderBy(project => project.Id, StringComparer.OrdinalIgnoreCase)
                .Select(project => new LabBaselineScenario
                {
                    Id = project.Id,
                    ExpectedStatus = project.ExpectedStatus,
                    ActualStatus = project.ActualStatus,
                    ContractHash = project.ContractHash,
                    SourcePassed = project.SourceTests.Passed,
                    SourceExpected = project.SourceTests.ExpectedPassed,
                    TargetPassed = project.TargetTests.Passed,
                    TargetExpected = project.TargetTests.ExpectedPassed,
                    TodoComments = project.Migration.TodoComments,
                    UnmappedTargets = project.Migration.UnmappedTargets,
                    UnsupportedActions = project.Migration.UnsupportedActions,
                    WarningFiles = project.Migration.Warnings,
                    QualityPassed = project.Quality.Passed,
                    OraclePassed = project.Oracle.Passed,
                    DiagnosticCategories = LabComparisonNormalizer.NormalizeSet(project.ProjectVerify.DiagnosticCategories, roots),
                    Diagnostics = LabComparisonNormalizer.NormalizeSet(
                        project.ProjectVerify.Diagnostics
                            .Concat(project.Migration.Issues)
                            .Concat(project.Migration.FailedStages.Select(stage => $"migration-stage:{stage}")),
                        roots),
                    SemanticChecks = LabComparisonNormalizer.SemanticCheckSignatures(project.Oracle, roots),
                    GeneratedSemanticHash = LabComparisonNormalizer.ComputeGeneratedSemanticHash(project.Migration.GeneratedFiles),
                    DurationMs = project.DurationMs
                })
                .ToArray()
        };
    }

}
