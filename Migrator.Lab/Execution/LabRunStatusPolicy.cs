using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabRunStatusPolicy
{
    static readonly string[] GeneralInfrastructureMarkers =
    {
        "no .net sdks were found",
        "the command could not be loaded",
        "a compatible installed .net sdk",
        "unable to load the service index",
        "nu1301",
        "cannot connect to proxy",
        "proxyerror",
        "name or service not known",
        "temporary failure in name resolution",
        "connection timed out",
        "network is unreachable",
        "no such host is known"
    };

    static readonly string[] BrowserInfrastructureMarkers =
    {
        "unable to obtain driver",
        "chromedriver executable needs to be available",
        "cannot find chrome binary",
        "chrome failed to start",
        "devtoolsactiveport file doesn't exist",
        "session not created",
        "selenium manager binary",
        "error sending request for url",
        "driver location must be a directory",
        "executable doesn't exist at",
        "please run the following command to download new browsers",
        "playwright install",
        "browsertype.launch"
    };

    static readonly LabRunStage[] SourceStages =
    {
        LabRunStage.SourceRestore,
        LabRunStage.SourceBuild,
        LabRunStage.SourceTest
    };

    public static LabStageOutcome ClassifySourceProcess(
        LabRunStage stage,
        LabProcessResult result,
        string combinedOutput)
    {
        if (result.TimedOut)
            return LabStageOutcome.TimedOut;
        if (result.StartFailed)
            return LabStageOutcome.InfrastructureFailure;
        if (result.ExitCode == 0)
            return LabStageOutcome.Passed;

        if (ContainsAny(combinedOutput, GeneralInfrastructureMarkers))
            return LabStageOutcome.InfrastructureFailure;
        if (stage == LabRunStage.SourceTest && ContainsAny(combinedOutput, BrowserInfrastructureMarkers))
            return LabStageOutcome.InfrastructureFailure;

        return LabStageOutcome.Failed;
    }

    public static LabStageOutcome ClassifyProjectVerifyProcess(
        LabProcessResult result,
        string combinedOutput)
    {
        if (result.TimedOut)
            return LabStageOutcome.TimedOut;
        if (result.StartFailed)
            return LabStageOutcome.InfrastructureFailure;
        if (result.ExitCode == 0)
            return LabStageOutcome.Passed;
        if (ContainsAny(combinedOutput, GeneralInfrastructureMarkers))
            return LabStageOutcome.InfrastructureFailure;
        return LabStageOutcome.Failed;
    }

    public static LabStageOutcome ClassifyTargetProcess(
        LabRunStage stage,
        LabProcessResult result,
        string combinedOutput)
    {
        if (result.TimedOut)
            return LabStageOutcome.TimedOut;
        if (result.StartFailed)
            return LabStageOutcome.InfrastructureFailure;
        if (result.ExitCode == 0)
            return LabStageOutcome.Passed;
        if (ContainsAny(combinedOutput, GeneralInfrastructureMarkers))
            return LabStageOutcome.InfrastructureFailure;
        if (stage == LabRunStage.TargetTest && ContainsAny(combinedOutput, BrowserInfrastructureMarkers))
            return LabStageOutcome.InfrastructureFailure;
        return LabStageOutcome.Failed;
    }

    public static ScenarioStatus ClassifyScenario(
        ScenarioStatus expectedStatus,
        IReadOnlyList<LabStageResult> stages,
        LabMigrationSummary migration,
        bool sourceContentPreserved)
    {
        var completedStages = stages
            .Concat(new[]
            {
                new LabStageResult { Stage = LabRunStage.ProjectVerify, Outcome = LabStageOutcome.Passed },
                new LabStageResult { Stage = LabRunStage.TargetBuild, Outcome = LabStageOutcome.Passed },
                new LabStageResult { Stage = LabRunStage.TargetTest, Outcome = LabStageOutcome.Passed },
                new LabStageResult { Stage = LabRunStage.SemanticOracle, Outcome = LabStageOutcome.Passed },
                new LabStageResult { Stage = LabRunStage.QualityEvaluation, Outcome = LabStageOutcome.Passed }
            })
            .ToArray();
        return ClassifyScenario(
            expectedStatus,
            completedStages,
            migration,
            new LabProjectVerifySummary { ReportPresent = true, Status = "passed", ExitCode = 0 },
            new LabQualityEvaluation { Passed = true },
            new LabSemanticOracleSummary { Passed = true },
            sourceContentPreserved);
    }

    public static ScenarioStatus ClassifyScenario(
        ScenarioStatus expectedStatus,
        IReadOnlyList<LabStageResult> stages,
        LabMigrationSummary migration,
        LabProjectVerifySummary projectVerify,
        LabQualityEvaluation quality,
        LabSemanticOracleSummary oracle,
        bool sourceContentPreserved)
    {
        var sourceStages = stages.Where(stage => SourceStages.Contains(stage.Stage)).ToArray();
        if (sourceStages.Any(stage => stage.Outcome is LabStageOutcome.InfrastructureFailure or LabStageOutcome.TimedOut))
            return ScenarioStatus.InfrastructureFailure;
        if (sourceStages.Any(stage => stage.Outcome == LabStageOutcome.Failed))
            return ScenarioStatus.SourceInvalid;

        var migrationStage = Find(stages, LabRunStage.Migration);
        if (migrationStage == null || migrationStage.Outcome == LabStageOutcome.Skipped)
            return ScenarioStatus.MigratorFailure;
        if (migrationStage.Outcome is LabStageOutcome.InfrastructureFailure or LabStageOutcome.TimedOut)
            return ScenarioStatus.InfrastructureFailure;
        if (!sourceContentPreserved || !migration.MandatoryArtifactsPresent)
            return ScenarioStatus.MigratorFailure;
        if (migration.FailedStages.Length > 0 || string.Equals(migration.OrchestrationStatus, "Failed", StringComparison.OrdinalIgnoreCase))
            return ScenarioStatus.Regression;
        if (migrationStage.Outcome == LabStageOutcome.Failed && migrationStage.ExitCode is not 1)
            return ScenarioStatus.MigratorFailure;

        var projectVerifyStage = Find(stages, LabRunStage.ProjectVerify);
        if (projectVerifyStage == null || projectVerifyStage.Outcome == LabStageOutcome.Skipped)
            return ScenarioStatus.MigratorFailure;
        if (projectVerifyStage.Outcome is LabStageOutcome.InfrastructureFailure or LabStageOutcome.TimedOut)
            return ScenarioStatus.InfrastructureFailure;
        if (!projectVerify.ReportPresent)
            return ScenarioStatus.MigratorFailure;
        if (projectVerifyStage.Outcome == LabStageOutcome.Failed
            || !string.Equals(projectVerify.Status, "passed", StringComparison.OrdinalIgnoreCase))
        {
            return ScenarioStatus.Regression;
        }

        foreach (var stageName in new[] { LabRunStage.TargetBuild, LabRunStage.TargetTest })
        {
            var stage = Find(stages, stageName);
            if (stage == null || stage.Outcome == LabStageOutcome.Skipped)
                return ScenarioStatus.Regression;
            if (stage.Outcome is LabStageOutcome.InfrastructureFailure or LabStageOutcome.TimedOut)
                return ScenarioStatus.InfrastructureFailure;
            if (stage.Outcome == LabStageOutcome.Failed)
                return ScenarioStatus.Regression;
        }

        if (!quality.Passed || !oracle.Passed)
            return ScenarioStatus.Regression;

        if (expectedStatus == ScenarioStatus.UnsupportedAsExpected)
        {
            var hasExpectedDiagnosticEvidence = migration.UnsupportedActions > 0
                || migration.TodoComments > 0
                || migration.UnmappedTargets > 0
                || migrationStage.ExitCode == 1
                || string.Equals(migration.OrchestrationStatus, "PassedWithWarnings", StringComparison.OrdinalIgnoreCase);
            return hasExpectedDiagnosticEvidence
                ? ScenarioStatus.UnsupportedAsExpected
                : ScenarioStatus.Regression;
        }

        var hasWarnings = migration.TodoComments > 0
            || migration.UnmappedTargets > 0
            || migration.UnsupportedActions > 0
            || migration.Warnings > 0
            || string.Equals(migration.OrchestrationStatus, "PassedWithWarnings", StringComparison.OrdinalIgnoreCase);
        return hasWarnings ? ScenarioStatus.PassWithWarnings : ScenarioStatus.Pass;
    }

    public static int GetSuiteExitCode(IEnumerable<LabScenarioRunResult> projects)
    {
        var statuses = projects.Select(project => project.ActualStatus).ToHashSet();
        if (statuses.Contains(ScenarioStatus.MigratorFailure))
            return 11;
        if (statuses.Contains(ScenarioStatus.Regression))
            return 10;
        if (statuses.Contains(ScenarioStatus.SourceInvalid))
            return 12;
        if (statuses.Contains(ScenarioStatus.InfrastructureFailure))
            return 13;
        if (statuses.Contains(ScenarioStatus.NonDeterministic))
            return 14;
        return 0;
    }

    static LabStageResult? Find(IEnumerable<LabStageResult> stages, LabRunStage stage) =>
        stages.LastOrDefault(item => item.Stage == stage);

    static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
