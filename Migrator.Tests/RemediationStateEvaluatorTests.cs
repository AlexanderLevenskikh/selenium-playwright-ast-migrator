using System.Text.Json;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class RemediationStateEvaluatorTests
{
    [Fact]
    public void Evaluate_AcceptsOnlyMeasuredImprovementWithoutRegression()
    {
        var before = State("a", unmapped: 4, todo: 4);
        var after = State("b", unmapped: 2, todo: 3);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "map submit target");

        Assert.Equal("ACCEPT", evaluation.Decision);
        Assert.False(evaluation.RollbackRequired);
        Assert.Contains(evaluation.Improvements, x => x.StartsWith("unmappedTargets 4->2", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RejectsNoProgressAndRequiresRollback()
    {
        var before = State("a", unmapped: 4, todo: 4);
        var after = State("b", unmapped: 4, todo: 4);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "rewrite helper");

        Assert.Equal("REJECT_NO_PROGRESS", evaluation.Decision);
        Assert.True(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_RejectsRegressionEvenWhenAnotherMetricImproves()
    {
        var before = State("a", syntax: 0, unmapped: 4);
        var after = State("b", syntax: 1, unmapped: 2);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "risky mapping");

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.True(evaluation.RollbackRequired);
        Assert.Contains(evaluation.Regressions, x => x.StartsWith("syntaxErrors 0->1", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RejectsVisitedStateAsCycleRegardlessOfCandidateLabel()
    {
        var before = State("b", unmapped: 2);
        var after = State("a", unmapped: 4);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "totally different wording", new[] { "a" });

        Assert.Equal("REJECT_CYCLE", evaluation.Decision);
        Assert.Equal("REMEDIATION_CYCLE_DETECTED", evaluation.Reason);
        Assert.True(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_RejectsChangedSourceSnapshot()
    {
        var before = State("a", source: "source-a");
        var after = State("b", source: "source-b");

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "mapping");

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.Equal("SOURCE_SNAPSHOT_CHANGED", evaluation.Reason);
    }


    [Fact]
    public void Rebaseline_ConfirmsToolUpgradeWithStableInputsAndNoRegression()
    {
        var before = State("old", unmapped: 4, todo: 5, config: "config", tool: "tool-old");
        var after = State("new", unmapped: 3, todo: 5, config: "config", tool: "tool-new");

        var evidence = RemediationRebaselineEvaluator.Evaluate(before, after);

        Assert.Equal("REBASELINE_CONFIRMED", evidence.Decision);
        Assert.Equal("NEW_TOOL_BASELINE_VERIFIED", evidence.Reason);
        Assert.Contains(evidence.Improvements, x => x == "unmappedTargets 4->3");
        Assert.NotEmpty(evidence.RebaselineSha256);
    }

    [Fact]
    public void Rebaseline_RejectsConfigDriftEvenAcrossToolUpgrade()
    {
        var before = State("old", config: "config-a", tool: "tool-old");
        var after = State("new", config: "config-b", tool: "tool-new");

        var evidence = RemediationRebaselineEvaluator.Evaluate(before, after);

        Assert.Equal("REBASELINE_REJECTED", evidence.Decision);
        Assert.Equal("CONFIG_IDENTITY_CHANGED", evidence.Reason);
    }

    [Fact]
    public void Rebaseline_RejectsNewToolMetricRegression()
    {
        var before = State("old", syntax: 0, unmapped: 4, config: "config", tool: "tool-old");
        var after = State("new", syntax: 1, unmapped: 3, config: "config", tool: "tool-new");

        var evidence = RemediationRebaselineEvaluator.Evaluate(before, after);

        Assert.Equal("REBASELINE_REJECTED", evidence.Decision);
        Assert.Equal("NEW_TOOL_REGRESSION", evidence.Reason);
        Assert.Contains(evidence.Regressions, x => x == "syntaxErrors 0->1");
    }

    [Fact]
    public void LoadRunState_ReadsVerifyWriterArtifactWithStringSeverityWithoutLosingMetrics()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-remediation-wire-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "verify"));

            const string sourceSha = "source";
            const string configSha = "config";
            const string targetSha = "target";
            const string toolSha = "tool";
            const string environmentSha = "environment";

            var verification = VerificationEvidence.Create(
                kind: "generated-verify",
                sourceSha256: sourceSha,
                configSha256: configSha,
                targetSha256: targetSha,
                toolSha256: toolSha,
                environmentSha256: environmentSha,
                status: "failed",
                exitCode: 3);

            var manifest = new RunManifest(
                SchemaVersion: "migrator-run-manifest/v2",
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Status: "failed",
                SourceSha256: sourceSha,
                SourceFiles: 1,
                ConfigSha256: configSha,
                TargetSha256: targetSha,
                Tool: new RunToolIdentity("test", null, "test", toolSha),
                Environment: new RunEnvironmentIdentity("test", "net10.0", "test", "x64", "en-US", "en-US", "LF", environmentSha),
                Verification: verification);

            File.WriteAllText(
                Path.Combine(root, "run-manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var issue = new VerifyIssue("syntax", IssueSeverity.Error, "boom", "Generated.cs", 7);
            var verifyReport = new VerifyReport(
                Status: "failed",
                FilesChecked: 1,
                GeneratedFilesChecked: 1,
                TodoComments: 12,
                PageTodoCalls: 3,
                UnsupportedActions: 4,
                UnmappedTargets: 5,
                RawExpressions: 6,
                SyntaxErrors: 7,
                ScopeWarnings: 0,
                ConfigWarnings: 0,
                PlaceholderLeftovers: 0,
                SuspiciousLiteralVariables: 0,
                DuplicateLocalVariables: 0,
                Files: new[] { new VerifyFileResult("Source.cs", "Generated.cs", null, "failed", new[] { issue }) },
                Issues: new[] { issue });

            File.WriteAllText(
                Path.Combine(root, "verify", "verify-report.json"),
                VerifyReportWriter.ToJson(verifyReport));

            var orchestration = new OrchestrationReport(
                Status: "failed",
                InputPath: "input",
                ConfigPath: "config",
                OutputPath: root,
                Stages: Array.Empty<OrchestrationStage>(),
                Metrics: new OrchestrationMetrics(1, 9, 1, 7, 12, 3, 0),
                Issues: Array.Empty<string>(),
                TopProposals: Array.Empty<string>(),
                RecommendedNextActions: Array.Empty<string>(),
                Warnings: Array.Empty<string>());

            File.WriteAllText(
                Path.Combine(root, "orchestration-report.json"),
                JsonSerializer.Serialize(orchestration, new JsonSerializerOptions { WriteIndented = true }));

            var state = RemediationStateEvaluator.LoadRunState(root);

            Assert.Equal(7, state.Defects.SyntaxErrors);
            Assert.Equal(4, state.Defects.UnsupportedActions);
            Assert.Equal(5, state.Defects.UnmappedTargets);
            Assert.Equal(6, state.Defects.RawExpressions);
            Assert.Equal(12, state.Defects.TodoComments);
            Assert.Equal(3, state.Defects.PageTodoCalls);
            Assert.Equal(9, state.Structure.TestsFound);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    static RemediationRunState State(
        string hash,
        int syntax = 0,
        int unsupported = 0,
        int unmapped = 0,
        int raw = 0,
        int todo = 0,
        int pageTodo = 0,
        string source = "source",
        string projectStatus = "passed",
        int projectDiagnostics = 0,
        string? config = null,
        string tool = "tool",
        string environment = "env")
        => new(
            RunPath: hash,
            SourceSha256: source,
            ConfigSha256: config ?? "config-" + hash,
            TargetSha256: "target-" + hash,
            ToolSha256: tool,
            EnvironmentSha256: environment,
            Defects: new RemediationDefectVector(syntax, unsupported, unmapped, raw, todo, pageTodo),
            Structure: new RemediationStructuralMetrics(TestsFound: 10, GeneratedFiles: 10),
            ProjectVerificationStatus: projectStatus,
            ProjectDiagnostics: projectDiagnostics,
            StateHash: hash);
}
