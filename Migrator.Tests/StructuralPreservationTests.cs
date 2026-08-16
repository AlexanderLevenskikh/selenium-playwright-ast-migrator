using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Tests;

public class StructuralPreservationTests
{
    static readonly HashSet<string> StructuralCategories = new(StringComparer.Ordinal)
    {
        "DuplicateSourceTestIdentity",
        "DuplicateTargetTestIdentity",
        "MissingTargetTest",
        "UnexpectedTargetTest",
        "VacuumTest",
        "VacuumSetUp",
        "AssertionLoss",
        "TestCaseLoss"
    };

    [Fact]
    public void ReorderedTests_ArePairedByStableIdentity_NotListPosition()
    {
        var source = File(
            tests: new[]
            {
                Test("First", new RawStatementAction(10, "First();")),
                Test("Second", new RawStatementAction(20, "Second();"))
            });
        var target = File(
            tests: new[]
            {
                Test("Second", new RawStatementAction(20, "SecondAsync();")),
                Test("First", new RawStatementAction(10, "FirstAsync();"))
            });

        var report = Verify(source, target);

        Assert.Empty(report.Issues.Where(issue => StructuralCategories.Contains(issue.Category)));
    }

    [Fact]
    public void MissingTargetTest_IsAnError()
    {
        var source = File(tests: new[]
        {
            Test("Kept", new RawStatementAction(10, "Kept();")),
            Test("Lost", new RawStatementAction(20, "Lost();"))
        });
        var target = File(tests: new[] { Test("Kept", new RawStatementAction(10, "KeptAsync();")) });

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "MissingTargetTest"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("Lost()", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnexpectedTargetTest_IsAnError()
    {
        var source = File(tests: new[] { Test("Original", new RawStatementAction(10, "Original();")) });
        var target = File(tests: new[]
        {
            Test("Original", new RawStatementAction(10, "OriginalAsync();")),
            Test("Invented", new RawStatementAction(20, "InventedAsync();"))
        });

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "UnexpectedTargetTest"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("Invented()", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSourceIdentity_IsRejectedInsteadOfPositionallyPaired()
    {
        var source = File(tests: new[]
        {
            Test("Duplicate", new RawStatementAction(10, "A();")),
            Test("Duplicate", new RawStatementAction(20, "B();"))
        });
        var target = File(tests: new[] { Test("Duplicate", new RawStatementAction(10, "AAsync();")) });

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "DuplicateSourceTestIdentity"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("Duplicate()", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTargetIdentity_IsRejectedInsteadOfArbitrarySelection()
    {
        var source = File(tests: new[] { Test("Duplicate", new RawStatementAction(10, "A();")) });
        var target = File(tests: new[]
        {
            Test("Duplicate", new RawStatementAction(10, "AAsync();")),
            Test("Duplicate", new RawStatementAction(20, "BAsync();"))
        });

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "DuplicateTargetTestIdentity"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("Duplicate()", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Overloads_ArePairedByNameAndNormalizedParameterTypes()
    {
        var source = File(tests: new[]
        {
            Test("Loads", new RawStatementAction(10, "LoadText();"), parameters: new[] { Parameter("Dictionary<string, int>", "input") }),
            Test("Loads", new RawStatementAction(20, "LoadNumber();"), parameters: new[] { Parameter("int", "value") })
        });
        var target = File(tests: new[]
        {
            Test("Loads", new RawStatementAction(20, "LoadNumberAsync();"), parameters: new[] { Parameter("int", "renamed") }),
            Test("Loads", new RawStatementAction(10, "LoadTextAsync();"), parameters: new[] { Parameter("Dictionary<string,int>", "other") })
        });

        var report = Verify(source, target);

        Assert.Empty(report.Issues.Where(issue => StructuralCategories.Contains(issue.Category)));
    }

    [Fact]
    public void NestedConditionalBehavior_CannotDisappearIntoContainerOnlyTarget()
    {
        var sourceNested = new ConditionalBlockAction(
            10,
            "ready",
            new TestAction[] { new RawStatementAction(11, "Submit();") },
            Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
            Array.Empty<TestAction>());
        var targetNested = new ConditionalBlockAction(
            10,
            "ready",
            new TestAction[] { new UnsupportedAction(11, "Submit();", "unmapped") },
            Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
            Array.Empty<TestAction>());

        var issue = Assert.Single(Verify(
            File(tests: new[] { Test("Nested", sourceNested) }),
            File(tests: new[] { Test("Nested", targetNested) }))
            .Issues.Where(item => item.Category == "VacuumTest"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("1 behavioral action", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupBehavior_CannotDisappear()
    {
        var source = File(setup: new TestAction[] { new RawStatementAction(5, "Login();") });
        var target = File(setup: Array.Empty<TestAction>());

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "VacuumSetUp"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("fixture setup", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertionCount_CannotDecrease()
    {
        var source = File(tests: new[]
        {
            Test("Checks",
                new AssertAreEqualAction(10, "1", "actualOne"),
                new AssertAreEqualAction(11, "2", "actualTwo"))
        });
        var target = File(tests: new[]
        {
            Test("Checks", new AssertAreEqualAction(10, "1", "actualOne"))
        });

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "AssertionLoss"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("preserves 1 of 2 source assertion", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertionsInsideAssertMultiple_AreCountedRecursively()
    {
        var sourceMultiple = new AssertMultipleAction(
            10,
            "Assert.Multiple(...) ",
            new TestAction[]
            {
                new AssertAreEqualAction(11, "1", "actualOne"),
                new AssertAreEqualAction(12, "2", "actualTwo")
            });
        var targetMultiple = new AssertMultipleAction(
            10,
            "Assert.Multiple(...) ",
            new TestAction[] { new AssertAreEqualAction(11, "1", "actualOne") });

        var issue = Assert.Single(Verify(
            File(tests: new[] { Test("Checks", sourceMultiple) }),
            File(tests: new[] { Test("Checks", targetMultiple) }))
            .Issues.Where(item => item.Category == "AssertionLoss"));

        Assert.Contains("1 of 2", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterizedTestCaseCount_CannotDecrease()
    {
        var source = File(tests: new[]
        {
            Test(
                "Cases",
                new RawStatementAction(10, "Run(value);"),
                cases: new[]
                {
                    new TestCaseData(new[] { "1" }, "[TestCase(1)]"),
                    new TestCaseData(new[] { "2" }, "[TestCase(2)]")
                },
                parameters: new[] { Parameter("int", "value") })
        });
        var target = File(tests: new[]
        {
            Test(
                "Cases",
                new RawStatementAction(10, "RunAsync(value);"),
                cases: new[] { new TestCaseData(new[] { "1" }, "[TestCase(1)]") },
                parameters: new[] { Parameter("int", "value") })
        });

        var issue = Assert.Single(Verify(source, target).Issues.Where(item => item.Category == "TestCaseLoss"));

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("1 of 2 source test case", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBuilder_TraversesConditionalAssertMultipleAndSetupRecursively()
    {
        var nestedTestUnsupported = new UnsupportedAction(12, "Nested();", "unsupported nested test");
        var nestedSetupUnsupported = new UnsupportedAction(6, "SetupNested();", "unsupported nested setup");
        var conditional = new ConditionalBlockAction(
            10,
            "ready",
            new TestAction[] { nestedTestUnsupported },
            Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
            Array.Empty<TestAction>());
        var multiple = new AssertMultipleAction(
            5,
            "Assert.Multiple(...) ",
            new TestAction[] { nestedSetupUnsupported });
        var model = File(
            setup: new TestAction[] { multiple },
            tests: new[] { Test("Nested", conditional) });

        var report = ReportBuilder.Build(model, "public class StructuralTestsPlaywright { }");

        Assert.Equal(2, report.UnsupportedCount);
        Assert.Equal(2, report.UnsupportedActions.Count());
        Assert.Equal(0, report.SuccessfullyConvertedTests);
    }

    static VerifyReport Verify(TestFileModel source, TestFileModel target)
    {
        const string generated = "public class StructuralTestsPlaywright { }";
        var result = new PipelineResult(source, target, generated, ReportBuilder.Build(target, generated));
        return VerifyRunner.Run(new[] { result }, config: null);
    }

    static TestFileModel File(
        IEnumerable<TestAction>? setup = null,
        IEnumerable<TestModel>? tests = null) =>
        new(
            FilePath: "Structural.cs",
            Namespace: "Sample.Tests",
            ClassName: "StructuralTests",
            BaseClassName: null,
            SetUpActions: setup ?? Array.Empty<TestAction>(),
            Tests: tests ?? Array.Empty<TestModel>());

    static TestModel Test(
        string name,
        TestAction action,
        IEnumerable<TestCaseData>? cases = null,
        IEnumerable<MethodParameterModel>? parameters = null) =>
        Test(name, new[] { action }, cases, parameters);

    static TestModel Test(
        string name,
        TestAction first,
        TestAction second) =>
        Test(name, new[] { first, second });

    static TestModel Test(
        string name,
        IEnumerable<TestAction> actions,
        IEnumerable<TestCaseData>? cases = null,
        IEnumerable<MethodParameterModel>? parameters = null) =>
        new(
            name,
            Category: null,
            CaseData: cases ?? Array.Empty<TestCaseData>(),
            Parameters: parameters ?? Array.Empty<MethodParameterModel>(),
            BodyActions: actions);

    static MethodParameterModel Parameter(string type, string name) =>
        new(type, name, DefaultValue: null);
}