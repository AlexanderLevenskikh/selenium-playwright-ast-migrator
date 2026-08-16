using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Tests;

public sealed class ExecutableSemanticPreservationTests
{
    [Fact]
    public void EmptyDotNetMethodMapping_IsSemanticNoOpAndVacuum()
    {
        var source = File(Test("Mapped", new RawStatementAction(10, "SourceAction();")));
        var target = File(Test("Mapped", Mapped(
            statements: Array.Empty<string>(),
            sourceLine: 10)));

        var report = Verify(source, target);

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void EmptyDotNetOverride_WinsOverNonDotNetFallbackForProof()
    {
        var source = File(Test("Mapped", new RawStatementAction(10, "SourceAction();")));
        var target = File(Test("Mapped", Mapped(
            statements: new[] { "await FallbackAsync();" },
            sourceLine: 10,
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["playwright-dotnet"] = Array.Empty<string>(),
                ["playwright-typescript"] = new[] { "await page.click();" }
            })));

        var report = Verify(source, target);

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void UnknownMappedPlaceholder_IsSemanticNoOp()
    {
        var source = File(Test("Mapped", new RawStatementAction(10, "SourceAction();")));
        var target = File(Test("Mapped", Mapped(
            statements: new[] { "await Page.GetByTestId({UNKNOWN}).ClickAsync();" },
            sourceLine: 10)));

        var report = Verify(source, target);

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void MixedMappedStatements_ReportPartialLossWithoutFalseVacuum()
    {
        var source = File(Test("Mapped", new RawStatementAction(10, "SourceAction();")));
        var target = File(Test("Mapped", Mapped(
            statements: new[]
            {
                "await Page.GetByTestId(\"save\").ClickAsync();",
                "await Page.GetByTestId({UNKNOWN}).ClickAsync();"
            },
            sourceLine: 10)));

        var report = Verify(source, target);

        Assert.Contains(report.Issues, issue => issue.Category == "PartialMappingLoss");
        Assert.DoesNotContain(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.DoesNotContain(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void RequiresReviewMapping_StillCountsAsExecutableWhenStatementIsValid()
    {
        var source = File(Test("Mapped", new RawStatementAction(10, "SourceAction();")));
        var target = File(Test("Mapped", Mapped(
            statements: new[] { "await Page.GetByTestId(\"save\").ClickAsync();" },
            sourceLine: 10,
            requiresReview: true)));

        var report = Verify(source, target);

        Assert.DoesNotContain(report.Issues, issue => issue.Category is "SemanticNoOp" or "PartialMappingLoss" or "VacuumTest");
    }

    [Fact]
    public void BrokenMappedExpressionAssertion_IsSemanticNoOpAndAssertionLoss()
    {
        var source = File(Test(
            "Assertion",
            new AssertAreEqualAction(10, "\"ok\"", "actual")));

        var target = File(Test(
            "Assertion",
            new MappedExpressionAssertionAction(
                10,
                "page.Status.Get().Should().Be(\"ok\")",
                "await Expect({TARGET}).ToHaveTextAsync({UNKNOWN})",
                targetExpr: TargetExpression.Mapped(
                    "page.Status",
                    "Page.GetByTestId(\"status\")",
                    TargetKind.PlaywrightLocator),
                sourceMethod: "Status")));

        var report = Verify(source, target);

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "AssertionLoss");
    }

    [Fact]
    public void UnsupportedAssertThat_IsNotCreditedAsExecutableAssertion()
    {
        var assertion = new AssertThatAction(
            10,
            "page.Status.Text",
            "Is.Not.Empty");

        var report = Verify(
            File(Test("Assertion", assertion)),
            File(Test("Assertion", assertion)));

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "AssertionLoss");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void LiteralEqualityAssertThat_OverMappedTargetLocal_RemainsExecutable()
    {
        var source = File(Test(
            "Assertion",
            new TestAction[]
            {
                new RawStatementAction(9, "var status = SourceStatus();"),
                new AssertThatAction(10, "status", "Is.EqualTo(\"ok\")")
            }));

        var target = File(Test(
            "Assertion",
            new TestAction[]
            {
                Mapped(
                    statements: new[] { "var status = await Page.GetByTestId(\"status\").InnerTextAsync();" },
                    sourceLine: 9,
                    resultVariable: "status"),
                new AssertThatAction(10, "status", "Is.EqualTo(\"ok\")")
            }));

        var report = Verify(source, target);

        Assert.DoesNotContain(report.Issues, issue => issue.Category is "SemanticNoOp" or "AssertionLoss" or "VacuumTest");
    }

    [Fact]
    public void UnresolvedControlStateAssertion_IsAssertionLoss()
    {
        var sourceAssertion = new ControlStateAssertionAction(
            10,
            TargetExpression.Mapped(
                "page.Checkbox",
                "Page.GetByTestId(\"checkbox\")",
                TargetKind.PlaywrightLocator),
            ControlStateAssertionKind.Checked,
            "page.Checkbox.IsChecked.Should().BeTrue()");

        var targetAssertion = new ControlStateAssertionAction(
            10,
            TargetExpression.Unresolved("page.Checkbox"),
            ControlStateAssertionKind.Checked,
            "page.Checkbox.IsChecked.Should().BeTrue()");

        var report = Verify(
            File(Test("Assertion", sourceAssertion)),
            File(Test("Assertion", targetAssertion)));

        Assert.Contains(report.Issues, issue => issue.Category == "AssertionLoss");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void UnresolvedCollectionParent_CannotHideExecutableLookingNestedBody()
    {
        var nestedSource = new RawStatementAction(11, "Submit();");
        var nestedTarget = new RawStatementAction(11, "SubmitAsync();");

        var sourceCollection = new CollectionForEachAction(
            10,
            "page.Rows",
            TargetExpression.Mapped(
                "page.Rows",
                "Page.GetByTestId(\"row\")",
                TargetKind.PlaywrightLocator),
            "row",
            new TestAction[] { nestedSource },
            "foreach (var row in page.Rows) { Submit(); }");

        var targetCollection = new CollectionForEachAction(
            10,
            "page.Rows",
            TargetExpression.Unresolved("page.Rows"),
            "row",
            new TestAction[] { nestedTarget },
            "foreach (var row in page.Rows) { Submit(); }");

        var report = Verify(
            File(Test("Collection", sourceCollection)),
            File(Test("Collection", targetCollection)));

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void ActionabilityElidedSourceWait_IsNotAFalseVacuum()
    {
        var wait = new WaitForAction(
            10,
            TargetExpression.Unresolved("page.Button"),
            sourceMethod: "WaitReady",
            kind: WaitForKind.ActionabilityElided);

        var report = Verify(
            File(Test("Wait", wait)),
            File(Test("Wait", wait)));

        Assert.DoesNotContain(report.Issues, issue => issue.Category == "VacuumTest");
        Assert.DoesNotContain(report.Issues, issue => issue.Category == "SemanticNoOp");
    }

    [Fact]
    public void ReviewRequiredWait_IsCommentOnlyAndCannotSatisfyBehaviorPreservation()
    {
        var wait = new WaitForAction(
            10,
            TargetExpression.Mapped(
                "page.Loader",
                "Page.GetByTestId(\"loader\")",
                TargetKind.PlaywrightLocator),
            sourceMethod: "WaitForBusinessState",
            kind: WaitForKind.ReviewRequired);

        var report = Verify(
            File(Test("Wait", wait)),
            File(Test("Wait", wait)));

        Assert.Contains(report.Issues, issue => issue.Category == "SemanticNoOp");
        Assert.Contains(report.Issues, issue => issue.Category == "VacuumTest");
    }

    [Fact]
    public void RemediationRejectsSemanticRegressionEvenWhenTodoCountImproves()
    {
        var before = RunState(
            "before",
            new RemediationDefectVector(
                0, 0, 0, 0, 5, 0,
                StructuralErrors: 0,
                SemanticLosses: 0));

        var after = RunState(
            "after",
            new RemediationDefectVector(
                0, 0, 0, 0, 4, 0,
                StructuralErrors: 0,
                SemanticLosses: 1));

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "remove one todo but lose semantics");

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.Contains(evaluation.Improvements, item => item.Contains("todoComments 5->4", StringComparison.Ordinal));
        Assert.Contains(evaluation.Regressions, item => item.Contains("semanticLosses 0->1", StringComparison.Ordinal));
    }

    [Fact]
    public void RemediationRejectsStructuralRegressionEvenWhenTodoCountImproves()
    {
        var before = RunState(
            "before",
            new RemediationDefectVector(
                0, 0, 0, 0, 5, 0,
                StructuralErrors: 0,
                SemanticLosses: 0));

        var after = RunState(
            "after",
            new RemediationDefectVector(
                0, 0, 0, 0, 4, 0,
                StructuralErrors: 1,
                SemanticLosses: 0));

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "remove one todo but drop structure");

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.Contains(evaluation.Regressions, item => item.Contains("structuralErrors 0->1", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticPreservationErrors_AreHardQualityGateFailures()
    {
        var source = File(Test("Mapped", new RawStatementAction(10, "SourceAction();")));
        var target = File(Test("Mapped", Mapped(
            statements: Array.Empty<string>(),
            sourceLine: 10)));

        var report = Verify(source, target);
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates: null);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void StructuralPreservationErrors_AreHardQualityGateFailures()
    {
        var source = File(Test("Lost", new RawStatementAction(10, "SourceAction();")));
        var target = new TestFileModel(
            FilePath: "Semantic.cs",
            Namespace: "Sample.Tests",
            ClassName: "SemanticTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: Array.Empty<TestModel>());

        var report = Verify(source, target);
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates: null);

        Assert.Contains(report.Issues, issue => issue.Category == "MissingTargetTest");
        Assert.Equal(1, exitCode);
    }

    static MappedMethodInvocationAction Mapped(
        IReadOnlyList<string> statements,
        int sourceLine,
        bool requiresReview = false,
        string? resultVariable = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? targetStatementsByTarget = null) =>
        new(
            sourceLine,
            "Source.Helper()",
            statements,
            requiresReview,
            targetExpr: null,
            sourceMethod: "Helper",
            resultVariable: resultVariable,
            targetStatementsByTarget: targetStatementsByTarget);

    static VerifyReport Verify(TestFileModel source, TestFileModel target)
    {
        const string generated = "public class SemanticTestsPlaywright { }";
        var result = new PipelineResult(
            source,
            target,
            generated,
            ReportBuilder.Build(target, generated));
        return VerifyRunner.Run(new[] { result }, config: null);
    }

    static TestFileModel File(TestModel test) =>
        new(
            FilePath: "Semantic.cs",
            Namespace: "Sample.Tests",
            ClassName: "SemanticTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[] { test });

    static TestModel Test(string name, TestAction action) =>
        Test(name, new[] { action });

    static TestModel Test(string name, IEnumerable<TestAction> actions) =>
        new(
            name,
            Category: null,
            CaseData: Array.Empty<TestCaseData>(),
            Parameters: Array.Empty<MethodParameterModel>(),
            BodyActions: actions);

    static RemediationRunState RunState(
        string stateHash,
        RemediationDefectVector defects) =>
        new(
            RunPath: stateHash,
            SourceSha256: "source",
            ConfigSha256: "config",
            TargetSha256: stateHash,
            ToolSha256: "tool",
            EnvironmentSha256: "environment",
            Defects: defects,
            Structure: new RemediationStructuralMetrics(TestsFound: 10, GeneratedFiles: 1),
            ProjectVerificationStatus: "passed",
            ProjectDiagnostics: 0,
            StateHash: stateHash);
}
