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
        "driver location must be a directory"
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

    public static ScenarioStatus ClassifyScenario(
        ScenarioStatus expectedStatus,
        IReadOnlyList<LabStageResult> stages,
        LabMigrationSummary migration,
        bool sourceContentPreserved)
    {
        var sourceStages = stages.Where(stage => stage.Stage != LabRunStage.Migration).ToArray();
        if (sourceStages.Any(stage => stage.Outcome is LabStageOutcome.InfrastructureFailure or LabStageOutcome.TimedOut))
            return ScenarioStatus.InfrastructureFailure;
        if (sourceStages.Any(stage => stage.Outcome == LabStageOutcome.Failed))
            return ScenarioStatus.SourceInvalid;

        var migrationStage = stages.FirstOrDefault(stage => stage.Stage == LabRunStage.Migration);
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

        if (migrationStage.ExitCode == 0
            && string.Equals(migration.OrchestrationStatus, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            return ScenarioStatus.Pass;
        }

        return ScenarioStatus.PassWithWarnings;
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

    static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
