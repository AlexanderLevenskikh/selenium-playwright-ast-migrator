using Migrator.Lab.Contracts;
using Migrator.Lab.Triage;

namespace Migrator.Tests;

// Regression coverage for the C3 audit finding: AUTO_FIX_ELIGIBLE must be decided from the
// FULL, untruncated set of unique suspected components, never from a value that was already
// bounded for a different (presentation/task-pack) reason.
[Trait("Layer", "Unit")]
public sealed class LabAutomationDispositionTests
{
    // The true, defect-proving regression test: two findings that share an identical
    // clustering fingerprint (so they group into one cluster) but implicate disjoint sets of
    // suspected files. The cluster's real, full, deduplicated footprint is 4 distinct files.
    // Before the C3 fix, BuildClusters's AutomationDisposition was purely
    // `group.All(finding => finding.AutomationDisposition == AutoFixEligible)` — it never
    // consulted the aggregated component breadth at all, truncated or otherwise. A cluster
    // could therefore never be rejected for spanning too many suspected files.
    [Fact]
    public void BuildClusters_RejectsAutoFix_WhenFullUniqueSuspectedComponentUnionExceedsBudget()
    {
        LabFailureEvidence Finding(string scenarioId, string[] suspectedComponents) => new()
        {
            ScenarioId = scenarioId,
            ExpectedStatus = ScenarioStatus.Pass,
            ActualStatus = ScenarioStatus.Regression,
            Stage = "semantic-oracle",
            DiagnosticCodes = new[] { "SEMANTIC_ORACLE" },
            SemanticDiffKinds = new[] { "event-sequence" },
            FeatureTags = new[] { "WebDriverWait" },
            SuspectedComponents = suspectedComponents,
            AutomationDisposition = LabAutomationDisposition.AutoFixEligible
        };

        var findings = new[]
        {
            Finding("scenario-a", new[] { "FileA.cs", "FileB.cs" }),
            Finding("scenario-b", new[] { "FileC.cs", "FileD.cs" })
        };

        var clusters = LabFailureTriageService.BuildClusters(findings);

        var cluster = Assert.Single(clusters);
        Assert.Equal(new[] { "scenario-a", "scenario-b" }, cluster.ScenarioIds);
        Assert.Equal(LabAutomationDisposition.ManualReview, cluster.AutomationDisposition);
        Assert.Equal(4, cluster.SuspectedComponents.Length);
    }

    // Companion/control case: the same clustering shape, but the two findings' suspected
    // components overlap enough that the full union stays within budget (3 distinct files).
    // Must remain AUTO_FIX_ELIGIBLE — the fix must not make the budget stricter than 3.
    [Fact]
    public void BuildClusters_AllowsAutoFix_WhenFullUniqueSuspectedComponentUnionIsWithinBudget()
    {
        LabFailureEvidence Finding(string scenarioId, string[] suspectedComponents) => new()
        {
            ScenarioId = scenarioId,
            ExpectedStatus = ScenarioStatus.Pass,
            ActualStatus = ScenarioStatus.Regression,
            Stage = "semantic-oracle",
            DiagnosticCodes = new[] { "SEMANTIC_ORACLE" },
            SemanticDiffKinds = new[] { "event-sequence" },
            FeatureTags = new[] { "WebDriverWait" },
            SuspectedComponents = suspectedComponents,
            AutomationDisposition = LabAutomationDisposition.AutoFixEligible
        };

        var findings = new[]
        {
            Finding("scenario-a", new[] { "FileA.cs", "FileB.cs" }),
            Finding("scenario-b", new[] { "FileB.cs", "FileC.cs" })
        };

        var clusters = LabFailureTriageService.BuildClusters(findings);

        var cluster = Assert.Single(clusters);
        Assert.Equal(3, cluster.SuspectedComponents.Length);
        Assert.Equal(LabAutomationDisposition.AutoFixEligible, cluster.AutomationDisposition);
    }

    // Boundary characterization of the extracted budget decision itself, run through the real
    // production method (not a reimplementation): 1 and 3 components stay eligible, 4 does not.
    [Theory]
    [InlineData(1, LabAutomationDisposition.AutoFixEligible)]
    [InlineData(3, LabAutomationDisposition.AutoFixEligible)]
    [InlineData(4, LabAutomationDisposition.ManualReview)]
    public void RecommendAutomationDisposition_AppliesBudgetToFullComponentSet(int componentCount, LabAutomationDisposition expected)
    {
        var project = new LabScenarioRunResult
        {
            Id = "boundary-scenario",
            ExpectedStatus = ScenarioStatus.Pass,
            ActualStatus = ScenarioStatus.Regression
        };
        var components = Enumerable.Range(1, componentCount).Select(i => $"File{i}.cs").ToArray();

        var disposition = LabFailureTriageService.RecommendAutomationDisposition(
            project,
            stage: "semantic-oracle",
            diagnostics: Array.Empty<string>(),
            features: Array.Empty<string>(),
            components: components);

        Assert.Equal(expected, disposition);
    }

    // The component-count budget must never override an earlier, independent disqualifier —
    // a single suspected component (well within budget) must still be forced to MANUAL_REVIEW
    // for each of these reasons, exactly as before the C3 fix.
    [Theory]
    [InlineData(ScenarioStatus.SourceInvalid)]
    [InlineData(ScenarioStatus.InfrastructureFailure)]
    [InlineData(ScenarioStatus.NonDeterministic)]
    public void RecommendAutomationDisposition_StatusDisqualifiers_StillWinOverComponentBudget(ScenarioStatus actualStatus)
    {
        var project = new LabScenarioRunResult
        {
            Id = "disqualified-scenario",
            ExpectedStatus = ScenarioStatus.Pass,
            ActualStatus = actualStatus
        };

        var disposition = LabFailureTriageService.RecommendAutomationDisposition(
            project,
            stage: "semantic-oracle",
            diagnostics: Array.Empty<string>(),
            features: Array.Empty<string>(),
            components: new[] { "OnlyFile.cs" });

        Assert.Equal(LabAutomationDisposition.ManualReview, disposition);
    }

    [Fact]
    public void RecommendAutomationDisposition_IntentionalUnsupportedFeature_StillWinsOverComponentBudget()
    {
        var project = new LabScenarioRunResult
        {
            Id = "unsupported-scenario",
            ExpectedStatus = ScenarioStatus.Pass,
            ActualStatus = ScenarioStatus.Regression
        };

        var disposition = LabFailureTriageService.RecommendAutomationDisposition(
            project,
            stage: "migration",
            diagnostics: Array.Empty<string>(),
            features: new[] { "IJavaScriptExecutor" },
            components: new[] { "OnlyFile.cs" });

        Assert.Equal(LabAutomationDisposition.ManualReview, disposition);
    }

    [Theory]
    [InlineData("infrastructure")]
    [InlineData("source-validation")]
    [InlineData("determinism")]
    public void RecommendAutomationDisposition_InfrastructureLikeStage_StillWinsOverComponentBudget(string stage)
    {
        var project = new LabScenarioRunResult
        {
            Id = "stage-disqualified-scenario",
            ExpectedStatus = ScenarioStatus.Pass,
            ActualStatus = ScenarioStatus.Regression
        };

        var disposition = LabFailureTriageService.RecommendAutomationDisposition(
            project,
            stage: stage,
            diagnostics: Array.Empty<string>(),
            features: Array.Empty<string>(),
            components: new[] { "OnlyFile.cs" });

        Assert.Equal(LabAutomationDisposition.ManualReview, disposition);
    }
}
