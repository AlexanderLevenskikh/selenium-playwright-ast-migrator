using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabRunStatusPolicyTests
{
    [Fact]
    public void SourceTestBrowserFailure_IsInfrastructureNotSourceInvalid()
    {
        var result = new LabProcessResult { ExitCode = 1 };

        var outcome = LabRunStatusPolicy.ClassifySourceProcess(
            LabRunStage.SourceTest,
            result,
            "session not created: Chrome failed to start: DevToolsActivePort file doesn't exist");

        Assert.Equal(LabStageOutcome.InfrastructureFailure, outcome);
    }

    [Fact]
    public void SourceBuildCompilerFailure_IsSourceFailure()
    {
        var result = new LabProcessResult { ExitCode = 1 };

        var outcome = LabRunStatusPolicy.ClassifySourceProcess(
            LabRunStage.SourceBuild,
            result,
            "Tests.cs(10,2): error CS1002: ; expected");

        Assert.Equal(LabStageOutcome.Failed, outcome);
    }

    [Fact]
    public void ExpectedUnsupported_RequiresVisibleDiagnosticEvidence()
    {
        var stages = AcceptedStages(migrationExitCode: 0);

        var withoutEvidence = LabRunStatusPolicy.ClassifyScenario(
            ScenarioStatus.UnsupportedAsExpected,
            stages,
            new LabMigrationSummary
            {
                MandatoryArtifactsPresent = true,
                OrchestrationStatus = "Passed"
            },
            sourceContentPreserved: true);
        var withEvidence = LabRunStatusPolicy.ClassifyScenario(
            ScenarioStatus.UnsupportedAsExpected,
            stages,
            new LabMigrationSummary
            {
                MandatoryArtifactsPresent = true,
                OrchestrationStatus = "Passed",
                TodoComments = 1
            },
            sourceContentPreserved: true);

        Assert.Equal(ScenarioStatus.Regression, withoutEvidence);
        Assert.Equal(ScenarioStatus.UnsupportedAsExpected, withEvidence);
    }

    [Fact]
    public void CompletedMigrationWithVerifyFailure_IsRegressionNotMigratorFailure()
    {
        var actual = LabRunStatusPolicy.ClassifyScenario(
            ScenarioStatus.Pass,
            AcceptedStages(migrationExitCode: 4),
            new LabMigrationSummary
            {
                MandatoryArtifactsPresent = true,
                OrchestrationStatus = "Failed",
                FailedStages = new[] { "verify" }
            },
            sourceContentPreserved: true);

        Assert.Equal(ScenarioStatus.Regression, actual);
    }

    [Fact]
    public void MissingMandatoryMigrationArtifacts_IsMigratorFailure()
    {
        var actual = LabRunStatusPolicy.ClassifyScenario(
            ScenarioStatus.Pass,
            AcceptedStages(migrationExitCode: 0),
            new LabMigrationSummary
            {
                MandatoryArtifactsPresent = false,
                OrchestrationStatus = "Passed"
            },
            sourceContentPreserved: true);

        Assert.Equal(ScenarioStatus.MigratorFailure, actual);
    }

    [Fact]
    public void SuiteExitCode_PreservesFailureNature()
    {
        Assert.Equal(0, LabRunStatusPolicy.GetSuiteExitCode(new[] { Project(ScenarioStatus.Pass), Project(ScenarioStatus.UnsupportedAsExpected) }));
        Assert.Equal(10, LabRunStatusPolicy.GetSuiteExitCode(new[] { Project(ScenarioStatus.Regression) }));
        Assert.Equal(11, LabRunStatusPolicy.GetSuiteExitCode(new[] { Project(ScenarioStatus.SourceInvalid), Project(ScenarioStatus.MigratorFailure) }));
        Assert.Equal(12, LabRunStatusPolicy.GetSuiteExitCode(new[] { Project(ScenarioStatus.SourceInvalid) }));
        Assert.Equal(13, LabRunStatusPolicy.GetSuiteExitCode(new[] { Project(ScenarioStatus.InfrastructureFailure) }));
        Assert.Equal(14, LabRunStatusPolicy.GetSuiteExitCode(new[] { Project(ScenarioStatus.NonDeterministic) }));
    }


    [Fact]
    public void ProjectVerifyCompilerFailure_IsRegression()
    {
        var stages = AcceptedStages(migrationExitCode: 0)
            .Concat(new[]
            {
                Stage(LabRunStage.ProjectVerify) with { Outcome = LabStageOutcome.Failed, ExitCode = 2 },
                Stage(LabRunStage.TargetBuild),
                Stage(LabRunStage.TargetTest)
            })
            .ToArray();

        var actual = LabRunStatusPolicy.ClassifyScenario(
            ScenarioStatus.Pass,
            stages,
            new LabMigrationSummary { MandatoryArtifactsPresent = true, OrchestrationStatus = "Passed" },
            new LabProjectVerifySummary { ReportPresent = true, Status = "failed", ExitCode = 2 },
            new LabQualityEvaluation { Passed = true },
            new LabSemanticOracleSummary { Passed = true },
            sourceContentPreserved: true);

        Assert.Equal(ScenarioStatus.Regression, actual);
    }

    [Fact]
    public void TargetMissingPlaywrightBrowser_IsInfrastructureFailure()
    {
        var process = new LabProcessResult { ExitCode = 1 };
        var outcome = LabRunStatusPolicy.ClassifyTargetProcess(
            LabRunStage.TargetTest,
            process,
            "Executable doesn't exist at C:/ms-playwright/chromium/headless_shell.exe. Please run playwright install.");

        Assert.Equal(LabStageOutcome.InfrastructureFailure, outcome);
    }

    [Fact]
    public void FailedQualityOrSemanticOracle_IsRegression()
    {
        var stages = AcceptedStages(migrationExitCode: 0)
            .Concat(new[]
            {
                Stage(LabRunStage.ProjectVerify),
                Stage(LabRunStage.TargetBuild),
                Stage(LabRunStage.TargetTest),
                Stage(LabRunStage.QualityEvaluation) with { Outcome = LabStageOutcome.Failed },
                Stage(LabRunStage.SemanticOracle) with { Outcome = LabStageOutcome.Failed }
            })
            .ToArray();

        var actual = LabRunStatusPolicy.ClassifyScenario(
            ScenarioStatus.Pass,
            stages,
            new LabMigrationSummary { MandatoryArtifactsPresent = true, OrchestrationStatus = "Passed" },
            new LabProjectVerifySummary { ReportPresent = true, Status = "passed", ExitCode = 0 },
            new LabQualityEvaluation { Passed = false },
            new LabSemanticOracleSummary { Passed = false },
            sourceContentPreserved: true);

        Assert.Equal(ScenarioStatus.Regression, actual);
    }

    static LabStageResult[] AcceptedStages(int migrationExitCode) => new[]
    {
        Stage(LabRunStage.SourceRestore),
        Stage(LabRunStage.SourceBuild),
        Stage(LabRunStage.SourceTest),
        Stage(LabRunStage.Migration) with { ExitCode = migrationExitCode }
    };

    static LabStageResult Stage(LabRunStage stage) => new()
    {
        Stage = stage,
        Outcome = LabStageOutcome.Passed
    };

    static LabScenarioRunResult Project(ScenarioStatus status) => new() { ActualStatus = status };
}
