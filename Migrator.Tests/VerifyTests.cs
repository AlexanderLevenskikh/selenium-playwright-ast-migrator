using System.IO;
using System.Linq;
using System.Reflection;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;
using Xunit;

namespace Migrator.Tests;

public class VerifyTests
{
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");

    /// <summary>
    /// Runs the full pipeline on Widget.cs to produce a PipelineResult with TODO comments,
    /// which we use as the base fixture for quality gate tests.
    /// </summary>
    PipelineResult GetWidgetResult()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        return pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));
    }

    /// <summary>
    /// Syntax checker that simulates a clean parse (no errors).
    /// </summary>
    static SyntaxCheckerDelegate CleanSyntaxChecker => _ => new List<(int, string)>();

    /// <summary>
    /// Syntax checker that simulates a syntax error in generated code.
    /// </summary>
    static SyntaxCheckerDelegate BrokenSyntaxChecker => _ => new List<(int, string)> { (42, "; expected") };

    #region Exit code: 0 — clean verify passes

    [Fact]
    public void Verify_ClassifiesCompileSafetyTodoDiagnostics()
    {
        var model = new TestFileModel(
            FilePath: "CompileSafety.cs",
            Namespace: "Test",
            ClassName: "CompileSafety",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: Array.Empty<TestModel>());

        var generated = string.Join("\n", new[]
        {
            "// TODO: raw statement requires manual review:",
            "// TODO: uses source-only identifier 'DataGenerator'",
            "// TODO: depends on unresolved symbol 'name'"
        });
        var migrationReport = ReportBuilder.Build(model, generated);
        var result = new PipelineResult(model, model, generated, migrationReport);

        var report = VerifyRunner.Run(new List<PipelineResult> { result }, config: null, CleanSyntaxChecker);

        Assert.Contains(report.Issues, i => i.Category == "RawDeclarationVariablesBlocked");
        Assert.Contains(report.Issues, i => i.Category == "SourceOnlyIdentifierUsage");
        Assert.Contains(report.Issues, i => i.Category == "BlockedSymbolUsage");
        Assert.Contains(report.Issues, i => i.Category == "DownstreamStatementBlocked");
    }

    [Fact]
    public void Verify_CleanReport_NoGates_ReturnsExitCode0()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var exitCode = VerifyRunner.ApplyQualityGates(report, null);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Verify_RealPipeline_SoftDefaults_Passes()
    {
        var result = GetWidgetResult();
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var config = System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(
            File.ReadAllText(adapterConfigPath));

        var report = VerifyRunner.Run(
            new List<PipelineResult> { result },
            config,
            CleanSyntaxChecker,
            scopeChecker: null);

        // Soft defaults: MaxTodoComments = int.MaxValue, so 8 TODOs don't fail
        var exitCode = VerifyRunner.ApplyQualityGates(report, null, report.Issues);
        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Exit code: 1 — quality gate threshold exceeded

    [Fact]
    public void Verify_MaxTodoCommentsZero_WithTodos_ReturnsExitCode1()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 5,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { MaxTodoComments = 0 };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Verify_MaxTodoCommentsZero_CleanCode_ReturnsExitCode0()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { MaxTodoComments = 0 };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Verify_MaxUnmappedExceeded_ReturnsExitCode1()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 10,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { MaxUnmappedTargets = 5 };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Verify_PageTodoCalls_FailOnPageTodo_ReturnsExitCode1()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 3,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { FailOnPageTodo = true };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Verify_PageTodoCalls_FailOnPageTodoDisabled_ReturnsExitCode0()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 3,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { FailOnPageTodo = false };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Verify_RealPipeline_MaxTodoCommentsZero_Fails()
    {
        var result = GetWidgetResult();
        var config = new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: Array.Empty<UiTargetMapping>(),
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            QualityGates: new QualityGatesConfig { MaxTodoComments = 0 });

        var report = VerifyRunner.Run(
            new List<PipelineResult> { result },
            config,
            CleanSyntaxChecker,
            scopeChecker: null);

        Assert.True(report.TodoComments > 0, "Widget should have TODO comments");
        var exitCode = VerifyRunner.ApplyQualityGates(report, config.QualityGates, report.Issues);
        Assert.Equal(1, exitCode);
    }

    #endregion

    #region Exit code: 2 — config error

    [Fact]
    public void Verify_ConfigError_NthWithoutIndex_ReturnsExitCode2()
    {
        var report = new VerifyReport(
            Status: "failed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 1,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: new[]
            {
                new VerifyIssue(
                    "Config", IssueSeverity.Error,
                    "UiTarget 'x' has Match='Nth' but no Index specified",
                    null, null)
            })
        ;

        var exitCode = VerifyRunner.ApplyQualityGates(report, null, report.Issues);
        Assert.Equal(2, exitCode);
    }

    #endregion

    #region Exit code: 3 — syntax error

    [Fact]
    public void Verify_SyntaxError_ReturnsExitCode3()
    {
        var report = new VerifyReport(
            Status: "failed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 2,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var exitCode = VerifyRunner.ApplyQualityGates(report, null);
        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void Verify_SyntaxError_Disabled_ReturnsExitCode0()
    {
        var report = new VerifyReport(
            Status: "failed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 2,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { FailOnInvalidGeneratedSyntax = false };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Verify_RealPipeline_BrokenSyntax_ReturnsExitCode3()
    {
        var result = GetWidgetResult();
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var config = System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(
            File.ReadAllText(adapterConfigPath));

        var report = VerifyRunner.Run(
            new List<PipelineResult> { result },
            config,
            BrokenSyntaxChecker,
            scopeChecker: null);

        Assert.True(report.SyntaxErrors > 0, "Should have syntax errors from broken checker");
        var exitCode = VerifyRunner.ApplyQualityGates(report, null, report.Issues);
        Assert.Equal(3, exitCode);
    }

    #endregion

    #region Combined: highest exit code wins

    [Fact]
    public void Verify_SyntaxAndGateFailure_ReturnsExitCode3()
    {
        var report = new VerifyReport(
            Status: "failed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 100,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 1,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { MaxTodoComments = 0 };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void Verify_ConfigAndGateFailure_ReturnsExitCode2()
    {
        var report = new VerifyReport(
            Status: "failed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 100,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 1,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: new[]
            {
                new VerifyIssue(
                    "Config", IssueSeverity.Error,
                    "UiTarget 'x' has Match='Nth' but no Index specified",
                    null, null)
            })
        ;

        var gates = new QualityGatesConfig { MaxTodoComments = 0 };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates, report.Issues);
        Assert.Equal(2, exitCode);
    }

    #endregion

    #region Raw expressions detection

    [Fact]
    public void Verify_RawExpressions_CountsUnresolvedTargets()
    {
        var result = GetWidgetResult();
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var config = System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(
            File.ReadAllText(adapterConfigPath));

        var report = VerifyRunner.Run(
            new List<PipelineResult> { result },
            config,
            CleanSyntaxChecker,
            scopeChecker: null);

        var unresolvedInActions = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
            .Concat(result.TargetModel.SetUpActions)
            .Count(a =>
                a is ClickAction c && c.Target.Kind == TargetKind.Unresolved ||
                a is SendKeysAction s && s.Target.Kind == TargetKind.Unresolved ||
                a is PressAction p && p.Target.Kind == TargetKind.Unresolved ||
                a is TextAssertionAction ta && ta.Target.Kind == TargetKind.Unresolved ||
                a is VisibilityAssertionAction va && va.Target.Kind == TargetKind.Unresolved ||
                a is WaitForAction wa && wa.Kind != WaitForKind.ActionabilityElided && wa.Target.Kind == TargetKind.Unresolved ||
                a is MappedMethodInvocationAction mmi && mmi.TargetStatements.Any(s => s.Contains("RawExpression")));

        Assert.Equal(unresolvedInActions, report.RawExpressions);
    }

    [Fact]
    public void Verify_ScopedParameterizedPlaceholders_AreCheckedWithoutGlobalParameterizedMappings()
    {
        var model = new TestFileModel(
            FilePath: "Scoped.cs",
            Namespace: "Test",
            ClassName: "Scoped",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    Array.Empty<TestAction>())
            });
        var report = new MigrationReport("Scoped.cs", 1, 1, Array.Empty<UnsupportedAction>(),
            "await Page.Locator(\"{name}\").ClickAsync();", 0, 0, 0, 0, 0, 0);
        var result = new PipelineResult(model, model, report.GeneratedOutput!, report);
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "Test",
            Scopes = new[]
            {
                new ProfileScope
                {
                    Name = "Scoped",
                    SourcePathPatterns = new[] { "Scoped.cs" },
                    ParameterizedMethods = new[]
                    {
                        new ParameterizedMethodMapping(
                            "Open({name})",
                            new[] { "await Page.Locator(\"{name}\").ClickAsync();" },
                            requiresReview: false)
                    }
                }
            }
        };

        var verify = VerifyRunner.Run(new List<PipelineResult> { result }, config, CleanSyntaxChecker);

        Assert.Equal(1, verify.PlaceholderLeftovers);
        Assert.Contains(verify.Issues, i => i.Category == "PlaceholderLeftover");
    }

    [Fact]
    public void Verify_MaxRawExpressionsZero_WithUnresolved_ReturnsExitCode1()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 3,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var gates = new QualityGatesConfig { MaxRawExpressions = 0 };
        var exitCode = VerifyRunner.ApplyQualityGates(report, gates);
        Assert.Equal(1, exitCode);
    }

    #endregion

    #region Soft defaults sanity

    [Fact]
    public void Verify_SoftDefaults_DoesNotFailOnHighTodoCount()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 9999,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var exitCode = VerifyRunner.ApplyQualityGates(report, null);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Verify_SoftDefaults_DoesNotFailOnUnmapped()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 500,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var exitCode = VerifyRunner.ApplyQualityGates(report, null);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Verify_SoftDefaults_DoesNotFailOnUnsupported()
    {
        var report = new VerifyReport(
            Status: "passed",
            FilesChecked: 1,
            GeneratedFilesChecked: 1,
            TodoComments: 0,
            PageTodoCalls: 0,
            UnsupportedActions: 50,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 0,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: Array.Empty<VerifyFileResult>(),
            Issues: Array.Empty<VerifyIssue>()
        );

        var exitCode = VerifyRunner.ApplyQualityGates(report, null);
        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Active TODO locator regression tests

    [Fact]
    public void Verify_ActivePageLocator_TODO_DetectedAsError()
    {
        var renderer = new PlaywrightDotNetRenderer();
        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    new TestAction[]
                    {
                        new ClickAction(1, TargetExpression.Unresolved("page.Button")),
                    })
            });
        var output = renderer.Render(model);

        // Unresolved targets render as commented TODO — not active Page.Locator("TODO:...")
        var activeLines = output.Split('\n').Where(l => !l.Trim().StartsWith("//")).ToList();
        var hasActiveTodoLocator = activeLines.Any(l => l.Contains("Page.Locator(\"TODO:"));
        Assert.False(hasActiveTodoLocator, "Unresolved targets should not generate active TODO locators");
    }

    [Theory]
    [InlineData("await Page.Locator(\"TODO: page.Button\").ClickAsync();")]
    [InlineData("await Page.GetByTestId(\"TODO_button\").ClickAsync();")]
    [InlineData("await Page.GetByText(\"TODO something\").ClickAsync();")]
    [InlineData("await Page.GetByRole(AriaRole.Button, new() { Name = \"TODO button\" }).ClickAsync();")]
    public void Verify_ActiveTodoLocator_CaughtByVerifyRunner(string activeTodoLine)
    {
        var model = new TestFileModel(
            FilePath: "ActiveTodo.cs",
            Namespace: "Test",
            ClassName: "ActiveTodo",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: Array.Empty<TestModel>());
        var generated = string.Join("\n", new[]
        {
            "using Microsoft.Playwright.NUnit;",
            "using NUnit.Framework;",
            "public class ActiveTodo : PageTest",
            "{",
            "\t[Test]",
            "\tpublic async Task T1()",
            "\t{",
            "\t\t" + activeTodoLine,
            "\t}",
            "}"
        });
        var result = new PipelineResult(model, model, generated, ReportBuilder.Build(model, generated));

        var report = VerifyRunner.Run(new List<PipelineResult> { result }, config: null, CleanSyntaxChecker);

        var activeTodoIssues = report.Issues.Where(i => i.Category == "ActiveTodoLocator").ToList();
        Assert.Single(activeTodoIssues);
        Assert.Equal(IssueSeverity.Error, activeTodoIssues[0].Severity);
    }

    [Fact]
    public void Verify_PageTodoCalls_Zero_ForWidgetOutput()
    {
        var result = GetWidgetResult();
        var report = VerifyRunner.Run(new List<PipelineResult> { result }, config: null, CleanSyntaxChecker);

        Assert.Equal(0, report.PageTodoCalls);
    }

    #endregion
}
