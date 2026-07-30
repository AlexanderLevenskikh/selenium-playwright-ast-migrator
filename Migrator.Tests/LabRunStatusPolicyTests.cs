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
