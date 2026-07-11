using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public class MigrationAgentRiskRoutingIterationTests
{
    [Fact]
    public void RiskRouter_EmitsExplainableDeterministicAssessment()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var router = Read("Migrator.Cli/Commands/MigrationAgentRiskRouter.cs");

        Assert.Contains("\"assess-agent-risk\" => RunAssessAgentRisk", command);
        Assert.Contains("migration-agent-risk-assessment/v1", router);
        Assert.Contains("riskScore", router);
        Assert.Contains("riskLevel", router);
        Assert.Contains("assessmentFingerprint", router);
        Assert.Contains("automaticContinuationAllowed", router);
        Assert.Contains("low", router);
        Assert.Contains("medium", router);
        Assert.Contains("high", router);
        Assert.Contains("critical", router);
    }

    [Fact]
    public void RiskRouter_UsesAdaptiveBudgetsWithoutWeakeningFinalRoles()
    {
        var router = Read("Migrator.Cli/Commands/MigrationAgentRiskRouter.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("AdaptiveAgentBudget", router);
        Assert.Contains("(\"fast\", \"low\")", router);
        Assert.Contains("[\"watchdog\"] = 0", router);
        Assert.Contains("reviewer:final", router);
        Assert.Contains("sentinel:final", router);
        Assert.Contains("ApplyAdaptiveBudget", runtime);
        Assert.Contains("Final review remains mandatory in every execution profile.", runtime);
        Assert.Contains("Final sentinel inspection remains mandatory before handoff.", runtime);
        Assert.Contains("adaptive-deterministic", fastPath);
        Assert.Contains("agent-lifecycle-budget-present", fastPath);
    }

    [Fact]
    public void RiskRouter_StopsCriticalEvidenceAndStaleDispatch()
    {
        var router = Read("Migrator.Cli/Commands/MigrationAgentRiskRouter.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("blocking: true", router);
        Assert.Contains("gate-weakening", router);
        Assert.Contains("evidence", router);
        Assert.Contains("AGENT_ROLE_CRITICAL_RISK", runtime);
        Assert.Contains("the adaptive risk assessment changed after routing", runtime);
        Assert.Contains("HUMAN_REVIEW_REQUIRED", runtime);
        Assert.Contains("stale-risk-dispatch-forbidden", fastPath);
        Assert.Contains("staleDispatchAllowed", fastPath);
    }

    [Fact]
    public void RiskRouter_IsVisibleInAgentWorkflowAndPerformanceEvidence()
    {
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var docs = Read("docs/migration-agent-risk-routing.md");
        var docsRu = Read("docs/migration-agent-risk-routing.ru.md");

        Assert.Contains("agent-risk-assessment.json", runtime);
        Assert.Contains("lifecycleBudgetStatus", runtime);
        Assert.Contains("assess-agent-risk", supervised);
        Assert.Contains("riskAssessmentFingerprint", contract);
        Assert.Contains("Adaptive agent risk routing", docs);
        Assert.Contains("Адаптивная маршрутизация риска", docsRu);
    }

    static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
