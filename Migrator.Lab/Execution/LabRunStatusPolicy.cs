using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabRunStatusPolicy
{
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

        // Source tests run with --no-build --no-restore. Treating arbitrary test output
        // as build/network infrastructure evidence can mask a real source failure when
        // an assertion happens to contain text such as "connection timed out". General
        // environment markers are therefore only authoritative for restore/build; the
        // test stage keeps the narrower browser-launch classification below.
        if ((stage is LabRunStage.SourceRestore or LabRunStage.SourceBuild)
            && InfrastructureFailureClassifier.ContainsGeneralInfrastructureMarker(combinedOutput))
        {
            return LabStageOutcome.InfrastructureFailure;
        }
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
        if (InfrastructureFailureClassifier.ContainsGeneralInfrastructureMarker(combinedOutput))
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
        // Target tests also run with --no-build --no-restore. A user/test assertion may
        // legitimately contain generic network words, so only target-build output may
        // use the broad infrastructure marker set. Browser bootstrap failures remain a
        // separate, explicit test-stage infrastructure signal.
        if (stage == LabRunStage.TargetBuild
            && InfrastructureFailureClassifier.ContainsGeneralInfrastructureMarker(combinedOutput))
        {
            return LabStageOutcome.InfrastructureFailure;
        }
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
            // Some stable fixtures intentionally prove that restore/build isolation
            // reports an infrastructure failure without misclassifying it as a
            // migration regression. Keep this expectation-scoped: ordinary projects
            // with the same diagnostics remain REGRESSION and cannot hide real defects.
            if (expectedStatus == ScenarioStatus.InfrastructureFailure
                && projectVerify.DiagnosticCategories.Contains(
                    "nuget-restore",
                    StringComparer.OrdinalIgnoreCase))
            {
                return ScenarioStatus.InfrastructureFailure;
            }

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
        var unexpected = projects
            .Where(project => project.ActualStatus != project.ExpectedStatus)
            .Select(project => project.ActualStatus)
            .ToHashSet();

        if (unexpected.Contains(ScenarioStatus.MigratorFailure))
            return LabExitCodes.MigratorFailure;
        if (unexpected.Contains(ScenarioStatus.Regression))
            return LabExitCodes.Regression;
        if (unexpected.Contains(ScenarioStatus.SourceInvalid))
            return LabExitCodes.SourceInvalid;
        if (unexpected.Contains(ScenarioStatus.InfrastructureFailure))
            return LabExitCodes.InfrastructureFailure;
        if (unexpected.Contains(ScenarioStatus.NonDeterministic))
            return LabExitCodes.NonDeterministic;
        if (unexpected.Count > 0)
            return LabExitCodes.Regression;
        return LabExitCodes.Accepted;
    }

    static LabStageResult? Find(IEnumerable<LabStageResult> stages, LabRunStage stage) =>
        stages.LastOrDefault(item => item.Stage == stage);

    static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
