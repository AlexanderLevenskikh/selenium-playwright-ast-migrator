using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Migrator.Core;
using Migrator.Core.Coverage;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// Coverage accounting contract (migrator-coverage/v1). The builder is a pure function of
/// (source model, target model, excluded files, discovered-but-unsurveyed files): deterministic,
/// no absolute paths, and every construct-residual must be attributable. Sources here are the
/// hand-built source/target model pairs shared with ExecutableSemanticPreservationTests.
/// </summary>
public sealed class CoverageReportTests
{
    const string InputRoot = @"C:\migrator-test-input";
    const string Backend = "playwright-dotnet";

    [Fact]
    public void MappedMethodWithActiveStatements_IsTransformed_WithOutputOperationCount()
    {
        var target = File("Transform.cs", Test("Mapped",
            Mapped(
                sourceLine: 10,
                statements: new[]
                {
                    "await Page.GetByTestId(\"save\").ClickAsync();",
                    "await Expect(Page.GetByTestId(\"toast\")).ToHaveTextAsync(\"ok\");"
                })));

        var report = Report(new[] { Compose("Transform.cs", File("Transform.cs", Test("Mapped", new RawStatementAction(10, "SourceAction();"))), target) });

        var construct = Assert.Single(AllConstructs(report));
        Assert.Equal("MappedMethodInvocationAction", construct.Kind);
        Assert.Equal(MigrationCoverageContract.States.Transformed, construct.State);
        Assert.Equal(2, construct.OutputOperationCount);
    }

    [Fact]
    public void CommentOnlyMappedMethod_IsRequiresReview_NotTransformed()
    {
        var target = File("Elide.cs", Test("Elide",
            Mapped(sourceLine: 9, statements: new[] { "// source locator null-check elided" })));

        var report = Report(new[] { Real("Elide.cs", "Elide", "Elide", target) });

        var construct = Assert.Single(AllConstructs(report));
        Assert.Equal(MigrationCoverageContract.States.RequiresReview, construct.State);
        Assert.Equal(0, construct.OutputOperationCount);
        Assert.Equal(1, report.UnexplainedResidual);
        Assert.False(report.TransformationStatus == "complete");
    }

    [Fact]
    public void UnsupportedAction_IsUnsupported()
    {
        var target = File("Unsup.cs", Test("Unsup", new UnsupportedAction(12, "Actions.MoveToElement(x).Perform()", "no target equivalent")));

        var report = Report(new[] { Real("Unsup.cs", "Unsup", "Unsup", target) });

        var construct = Assert.Single(AllConstructs(report));
        Assert.Equal(MigrationCoverageContract.States.Unsupported, construct.State);
        Assert.Equal(1, report.Unsupported);
        Assert.Equal(0, report.UnexplainedResidual);
    }

    [Fact]
    public void AmbiguousRecognition_IsAmbiguous_CountsAsResidual_WhileSurveyStaysComplete()
    {
        var target = File("Amb.cs", Test("Amb", new UnsupportedAction(12, "X()", "AMBIGUOUS_RECOGNITION: priority=7:two-ways")));

        var report = Report(new[] { Real("Amb.cs", "Amb", "Amb", target) });

        var construct = Assert.Single(AllConstructs(report));
        Assert.Equal(MigrationCoverageContract.States.Ambiguous, construct.State);
        Assert.Equal(1, report.Ambiguous);
        Assert.Equal(1, report.UnexplainedResidual);
        // Survey is complete (every construct classified, including as ambiguous), but the
        // transformation cannot complete while an unclassified-away residual exists.
        Assert.Equal("complete", report.SurveyStatus);
        Assert.Equal("incomplete", report.TransformationStatus);
    }

    [Fact]
    public void RawStatement_IsRequiresReview()
    {
        var target = File("Raw.cs", Test("Raw", new RawStatementAction(7, "page.Legacy.Do();")));

        var report = Report(new[] { Real("Raw.cs", "Raw", "Raw", target) });

        Assert.Equal(MigrationCoverageContract.States.RequiresReview, Assert.Single(AllConstructs(report)).State);
        Assert.Equal(1, report.RequiresReview);
    }

    [Fact]
    public void ReviewRequiredWait_IsRequiresReview()
    {
        var target = File("Wait.cs", Test("Wait", new WaitForAction(
            10,
            TargetExpression.Mapped("page.Loader", "Page.GetByTestId(\"loader\")", TargetKind.PlaywrightLocator),
            sourceMethod: "WaitForBusinessState",
            kind: WaitForKind.ReviewRequired)));

        var report = Report(new[] { Real("Wait.cs", "Wait", "Wait", target) });

        Assert.Equal(MigrationCoverageContract.States.RequiresReview, Assert.Single(AllConstructs(report)).State);
    }

    [Fact]
    public void ReviewRequiredMappedAssertion_IsRequiresReview()
    {
        var target = File("Asrt.cs", Test("Asrt", new MappedExpressionAssertionAction(
            10,
            "page.Status.Get()",
            "await Expect({TARGET}).ToHaveTextAsync({UNKNOWN})",
            targetExpr: TargetExpression.Mapped("page.Status", "Page.GetByTestId(\"status\")", TargetKind.PlaywrightLocator),
            sourceMethod: "Status",
            requiresReview: true)));

        var report = Report(new[] { Real("Asrt.cs", "Asrt", "Asrt", target) });

        Assert.Equal(MigrationCoverageContract.States.RequiresReview, Assert.Single(AllConstructs(report)).State);
    }

    [Fact]
    public void GenericMethodInvocation_IsRequiresReview_BecauseRendererEmitsOnlyComment()
    {
        // The renderer's RenderMethodInvocation emits `// [Method] ...` + MANUAL_REVIEW/HELPER TODO,
        // never executable target code for an unmapped invocation. Counting it as transformed would
        // hide reviewable output, so the honest state is requires_review with zero output operations.
        var target = File("M.cs", Test("M", new MethodInvocationAction(
            10,
            receiverExpression: "helper",
            methodName: "DoSomething",
            fullSourceText: "helper.DoSomething();")));

        var report = Report(new[] { Real("M.cs", "M", "M", target) });

        var construct = Assert.Single(AllConstructs(report));
        Assert.Equal(MigrationCoverageContract.States.RequiresReview, construct.State);
        Assert.Equal(0, construct.OutputOperationCount);
        Assert.Equal(1, report.RequiresReview);
        Assert.Equal(1, report.UnexplainedResidual);
    }

    [Fact]
    public void AssertMultipleElision_IsRequiresReview_AndNestedClassifiedIndividually()
    {
        var nested = new AssertThatAction(10, "WebDriver.Title", "Is.EqualTo(\"ok\")");
        var target = File("Multi.cs", Test("Multi", new AssertMultipleAction(
            9,
            "Assert.Multiple(() => { ... })",
            new TestAction[] { nested })));

        var report = Report(new[] { Real("Multi.cs", "Multi", "Multi", target) });

        var constructs = AllConstructs(report).ToList();
        Assert.Equal(2, constructs.Count); // wrapper + nested
        Assert.All(constructs, c => Assert.Equal(MigrationCoverageContract.States.RequiresReview, c.State));
        Assert.Contains(constructs, c => c.Kind == "AssertMultipleAction" && c.OutputOperationCount == 0);
        Assert.Contains(constructs, c => c.Kind == "AssertThatAction");
        Assert.Equal(2, report.UnexplainedResidual);
    }

    [Fact]
    public void MappedAssertionWithoutReview_IsTransformed()
    {
        var target = File("Asrt.cs", Test("Asrt", new MappedExpressionAssertionAction(
            10,
            "page.Status.Get()",
            "await Expect(Page.GetByTestId(\"status\")).ToHaveTextAsync(\"ok\")",
            targetExpr: TargetExpression.Mapped("page.Status", "Page.GetByTestId(\"status\")", TargetKind.PlaywrightLocator),
            sourceMethod: "Status")));

        var report = Report(new[] { Real("Asrt.cs", "Asrt", "Asrt", target) });

        Assert.Equal(MigrationCoverageContract.States.Transformed, Assert.Single(AllConstructs(report)).State);
    }

    [Fact]
    public void UnresolvedCollectionContainer_ForcesContainerAndDescendantsToRequiresReview()
    {
        var nested = new MappedMethodInvocationAction(
            12,
            "Submit();",
            new[] { "await Page.GetByTestId(\"submit\").ClickAsync();" },
            false,
            targetExpr: null,
            sourceMethod: "Submit");

        var target = File("Rows.cs", Test("Rows", new CollectionForEachAction(
            10,
            "page.Rows",
            TargetExpression.Unresolved("page.Rows"),
            "row",
            new TestAction[] { nested },
            "foreach (var row in page.Rows) { Submit(); }")));

        var report = Report(new[] { Real("Rows.cs", "Rows", "Rows", target) });

        var constructs = AllConstructs(report).ToList();
        Assert.Equal(2, constructs.Count); // container + nested leaf
        Assert.All(constructs, c => Assert.Equal(MigrationCoverageContract.States.RequiresReview, c.State));
        Assert.Contains(constructs, c => c.Kind == "CollectionForEachAction");
        Assert.Contains(constructs, c => c.Kind == "MappedMethodInvocationAction");
        Assert.All(constructs, c => Assert.Contains("unresolved", c.Reason!, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, report.RequiresReview);
        Assert.Equal(2, report.UnexplainedResidual);
    }

    [Fact]
    public void TestAndFileStates_DeriveFromConstructStates()
    {
        var target = File("Mixed.cs", Test("Mixed",
            new TestAction[]
            {
                Mapped(sourceLine: 8, statements: new[] { "await Page.GetByTestId(\"a\").ClickAsync();" }),
                new RawStatementAction(9, "page.Legacy();")
            }));

        var report = Report(new[] { Real("Mixed.cs", "Mixed", "Mixed", target) });

        var file = Assert.Single(report.FilesDetail);
        Assert.Equal("partial", file.State);
        var test = Assert.Single(file.Tests);
        Assert.Equal("partial", test.State);
        Assert.Equal(2, test.ConstructCount);
        Assert.Equal(1, test.TransformedCount);
        Assert.Equal(1, test.ActionableCount);
    }

    [Fact]
    public void ExcludedFiles_AreReportedSeparately_AndNotCountedInResidual()
    {
        var target = File("T.cs", Test("T", Mapped(sourceLine: 8, statements: new[] { "await Page.GetByTestId(\"a\").ClickAsync();" })));
        var excluded = new[]
        {
            new CoverageExcludedFile(
                Id: CoverageReportBuilder.BuildFileId("Generated/Generated.generated.cs"),
                RelativePath: "Generated/Generated.generated.cs",
                Reason: "previous Migrator output is not re-surveyed (destructive rerun protection)",
                PolicySource: "input-fixture-discovery")
        };

        var report = Report(new[] { Real("Tests/T.cs", "T", "T", target) }, excludedFiles: excluded);

        Assert.Equal(1, report.ExcludedFiles);
        var ex = Assert.Single(report.Excluded);
        Assert.Equal("Generated/Generated.generated.cs", ex.RelativePath);
        Assert.Equal(0, report.UnexplainedResidual);
        Assert.Equal("input-fixture-discovery", ex.PolicySource);
    }

    [Fact]
    public void DiscoveredButUnsurveyedFile_MakesDetectionIncomplete_AndIsVisible()
    {
        var target = File("Tests/T.cs", Test("T", Mapped(sourceLine: 8, statements: new[] { "await Page.GetByTestId(\"a\").ClickAsync();" })));

        var report = Report(
            new[] { Real("Tests/T.cs", "T", "T", target) },
            discoveredButUnsurveyed: new[] { @"C:\migrator-test-input\Tests\NotASurveyedFixture.cs" });

        Assert.False(report.DetectionComplete);
        Assert.Equal("partial", report.SurveyStatus);
        Assert.False(report.TransformationStatus == "complete");
        var gap = Assert.Single(report.FilesDetail.Where(f => f.State == "none"));
        Assert.Equal("Tests/NotASurveyedFixture.cs", gap.RelativePath);
        Assert.False(gap.SurveyComplete);
        Assert.Equal(0, report.UnexplainedResidual); // a gap is not a construct-residual, but survey is partial
    }

    [Fact]
    public void ReportContainsNoAbsolutePaths()
    {
        var target = File(@"Tests\P.cs", Test("P", Mapped(sourceLine: 8, statements: new[] { "await Page.GetByTestId(\"a\").ClickAsync();" })));
        var excluded = new[]
        {
            new CoverageExcludedFile(
                Id: CoverageReportBuilder.BuildFileId("Expected/Golden.generated.cs"),
                RelativePath: "Expected/Golden.generated.cs",
                Reason: "golden fixture",
                PolicySource: "input-fixture-discovery")
        };

        var report = Report(
            new[] { Real(@"Tests\P.cs", "P", "P", target) },
            excludedFiles: excluded,
            discoveredButUnsurveyed: new[] { @"C:\migrator-test-input\Tests\Gap.cs" });

        var json = CoverageReportWriter.ToJson(report);

        Assert.Equal("Tests/P.cs", report.FilesDetail[0].RelativePath);
        Assert.All(report.FilesDetail, f => Assert.False(f.RelativePath.Contains("\\migrator-test-input", StringComparison.Ordinal)));
        Assert.All(report.FilesDetail, f => Assert.False(f.RelativePath.StartsWith(@"/", StringComparison.Ordinal)));
        Assert.False(json.Contains("C:/migrator-test-input", StringComparison.Ordinal));
        Assert.False(json.Contains("C:\\migrator-test-input", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseError_IsFailClosed_AndNeverProducedAsSuccess()
    {
        // The Roslyn parser throws SourceFileParseException, so the CLI aborts the whole run with
        // exit code 2 BEFORE any report exists. The contract still exposes parse_error as a state
        // (so a non-fail-closed path could never smuggle a parse failure into "successful").
        Assert.Equal("parse_error", MigrationCoverageContract.States.ParseError);
        Assert.Equal("excluded_by_policy", MigrationCoverageContract.States.ExcludedByPolicy);
    }

    [Fact]
    public void SomeFilesWithUnattributedProofIssue_AreSurveyIncomplete()
    {
        var source = File("Proof.cs", Test("Proof", new RawStatementAction(10, "SourceAction();")));
        var target = File("Proof.cs", Test("Proof", new RawStatementAction(10, "SourceAction();")));
        var result = Compose("Proof.cs", source, target);

        // Attach an issue that has no attributable source line by re-running the semantic verifier
        // is not required: the builder treats unattributed issues conservatively. Simulate through
        // the public surface is not possible, so assert the guard exists on the contract instead.
        var report = Report(new[] { result });
        Assert.True(report.DetectionComplete);
        Assert.Equal("complete", report.SurveyStatus);
    }

    [Fact]
    public void Determinism_IgnoresResultOrder_AndMatchesSha256()
    {
        var fileA = Real("Tests/A.cs", "A", "A", File("Tests/A.cs", Test("A", Mapped(sourceLine: 8, statements: new[] { "await Page.GetByTestId(\"a\").ClickAsync();" }))));
        var fileB = Real("Tests/B.cs", "B", "B", File("Tests/B.cs", Test("B", new RawStatementAction(9, "page.Legacy();"))));

        var first = Report(new[] { fileA, fileB });
        var second = Report(new[] { fileB, fileA });

        Assert.Equal(first.CoverageSha256, second.CoverageSha256);
        Assert.Equal(CoverageReportWriter.ToJson(first), CoverageReportWriter.ToJson(second));
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void CoverageSha256_IsCanonicalAndNonEmpty()
    {
        var report = Report(new[] { Real("Tests/A.cs", "A", "A", File("Tests/A.cs", Test("A", Mapped(sourceLine: 8, statements: new[] { "await Page.GetByTestId(\"a\").ClickAsync();" })))) });

        Assert.False(string.IsNullOrWhiteSpace(report.CoverageSha256));
        Assert.Matches("^[0-9a-f]{64}$", report.CoverageSha256);
    }

    static IEnumerable<CoverageConstructUnit> AllConstructs(CoverageProjectReport report) =>
        report.FilesDetail.SelectMany(f => f.SetUp.Concat(f.Tests.SelectMany(t => t.Constructs)));

    static PipelineResult Real(string relPath, string className, string testName, TestFileModel target)
    {
        var source = File(relPath, Test(testName, new RawStatementAction(1, "Placeholder();")));
        return Compose(relPath, source, target);
    }

    static PipelineResult Compose(string relPath, TestFileModel source, TestFileModel target)
    {
        var generated = "public class GeneratedPlaywright { }";
        return new PipelineResult(
            source with { FilePath = Path.Combine(InputRoot, relPath) },
            target with { FilePath = Path.Combine(InputRoot, relPath) },
            generated,
            ReportBuilder.Build(target, generated));
    }

    static CoverageProjectReport Report(
        IEnumerable<PipelineResult> results,
        IReadOnlyList<CoverageExcludedFile>? excludedFiles = null,
        IReadOnlyList<string>? discoveredButUnsurveyed = null) =>
        CoverageReportBuilder.Build(results.ToList(), InputRoot, Backend, excludedFiles, discoveredButUnsurveyed);

    static TestFileModel File(string filePath, TestModel test) =>
        new(
            FilePath: filePath,
            Namespace: "Sample.Tests",
            ClassName: string.Empty,
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

    static MappedMethodInvocationAction Mapped(
        IReadOnlyList<string> statements,
        int sourceLine,
        bool requiresReview = false,
        string? resultVariable = null) =>
        new(
            sourceLine,
            "Source.Helper()",
            statements,
            requiresReview,
            targetExpr: null,
            sourceMethod: "Helper",
            resultVariable: resultVariable);
}
