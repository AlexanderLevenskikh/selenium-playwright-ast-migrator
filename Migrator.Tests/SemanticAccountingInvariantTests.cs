using System;
using System.Collections.Generic;
using System.Linq;
using Migrator.Core;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// Phase A.1 — semantic accounting invariant.
///
/// PROTECTS: Sem+Syn+Unsup == TotalActions over the flattened action model, and that
/// StructuralContainers is an independent structure dimension (a subset of TotalActions),
/// not a confidence bucket. This pins the fix for the ReportBuilder double-count where
/// Semantic+SyntaxFallback (flattened, containers counted as nodes + children) exceeded
/// the CLI's top-level ActionsFound.
/// </summary>
public class SemanticAccountingInvariantTests
{
    [Fact]
    public void FlatModel_ConfidenceBucketsSumToTotalActions()
    {
        var model = File(
            tests: new[]
            {
                Test("Flat",
                    new ClickAction(1, Mapped("a.Save", "save")),
                    new SendKeysAction(2, Unresolved("WebDriver.FindElement(By.Id(\"name\"))"), "\"Alex\""),
                    new UnsupportedAction(3, "Legacy();", "test line"))
            });

        var report = ReportBuilder.Build(model, "public class X { }");

        Assert.Equal(3, report.TotalActions);
        Assert.Equal(2, report.SemanticActions);
        Assert.Equal(0, report.SyntaxFallbackActions);
        Assert.Equal(1, report.UnsupportedCount);
        Assert.Equal(0, report.StructuralContainers);
        Assert.True(ReportBuilder.AccountingInvariantHolds(report));
    }

    [Fact]
    public void NestedContainers_CountedAsStructuralNodesNotConfidenceBuckets()
    {
        var conditional = new ConditionalBlockAction(
            1,
            "ready",
            new TestAction[]
            {
                new ClickAction(2, Mapped("a.Save", "save")),
                new TextAssertionAction(3, Mapped("a.Toast", "toast"), TextAssertionKind.TextEquals, "\"Saved\"")
            },
            Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
            Array.Empty<TestAction>());

        var forEach = new CollectionForEachAction(
            4,
            "rows",
            Unresolved("rows"),
            "row",
            new TestAction[] { new ClickAction(5, Unresolved("row.Save")) },
            "foreach (var row in rows)",
            RecognitionConfidence.SyntaxFallback);

        var multiple = new AssertMultipleAction(
            6,
            "Assert.Multiple(() => { ... }) ",
            new TestAction[]
            {
                new TextAssertionAction(7, Unresolved("a.Name"), TextAssertionKind.TextEquals, "\"x\""),
                new VisibilityAssertionAction(8, Unresolved("a.Loader"), VisibilityKind.Hidden)
            });

        var model = File(
            tests: new[] { Test("Nested", conditional, forEach, multiple) });

        var report = ReportBuilder.Build(model, "public class X { }");

        // Flattened total: 3 containers + 5 nested leaves.
        Assert.Equal(8, report.TotalActions);
        Assert.Equal(3, report.StructuralContainers);

        // Every flattened node carries exactly one confidence; the buckets partition the total.
        Assert.Equal(report.TotalActions, report.SemanticActions + report.SyntaxFallbackActions + report.UnsupportedCount);
        Assert.True(report.StructuralContainers <= report.TotalActions);
        Assert.True(ReportBuilder.AccountingInvariantHolds(report));
    }

    [Fact]
    public void SetUpContainers_AreIncludedInFlattenedAccounting()
    {
        var multiple = new AssertMultipleAction(
            5,
            "Assert.Multiple(...) ",
            new TestAction[]
            {
                new VisibilityAssertionAction(6, Unresolved("a.Loader"), VisibilityKind.Hidden),
                new ClickAction(7, Mapped("a.Submit", "submit"))
            });

        var model = File(
            setup: new TestAction[] { multiple },
            tests: new[] { Test("Setup", new ClickAction(1, Mapped("a.Open", "open"))) });

        var report = ReportBuilder.Build(model, "public class X { }");

        Assert.Equal(4, report.TotalActions);
        Assert.Equal(1, report.StructuralContainers);
        Assert.True(ReportBuilder.AccountingInvariantHolds(report));
    }

    static TargetExpression Mapped(string source, string target) =>
        TargetExpression.Mapped(source, target, TargetKind.PlaywrightLocator);

    static TargetExpression Unresolved(string source) => TargetExpression.Unresolved(source);

    static TestModel Test(string name, params TestAction[] actions) =>
        new(name, "QuickRunning", Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(), actions);

    static TestFileModel File(TestModel[]? tests = null, TestAction[]? setup = null) =>
        new(
            FilePath: "Test.cs",
            Namespace: "Tests",
            ClassName: "Test",
            BaseClassName: null,
            SetUpActions: setup ?? Array.Empty<TestAction>(),
            Tests: tests ?? Array.Empty<TestModel>());
}
