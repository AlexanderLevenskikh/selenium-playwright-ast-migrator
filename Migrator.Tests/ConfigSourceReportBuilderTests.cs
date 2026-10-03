using System.Linq;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public class ConfigSourceReportBuilderTests
{
    static (string Path, string Text)[] SourceFiles() => new[]
    {
        ("A.cs", "public void M() { WaitVisible(); WaitForText(\"ok\"); By.Id(\"login\"); NavigateToMain(); }"),
        ("B.cs", "public int GetResult() => 1; public void BillingScaffold() { } public void M2() { LoginPage p = new LoginPage(); }"),
        ("C.cs", "class X { public void ObsoleteHelper1() { } public void Usage() { ObsoleteHelper1(); } }")
    };

    static ProjectAdapterConfig SectionsConfig()
    {
        return new ProjectAdapterConfig
        {
            UiTargets = new[]
            {
                new UiTargetMapping { SourceExpression = "By.Id(\"login\")", TargetExpression = "Page.Locator(\"#login\")", TargetKind = "Locator" }
            },
            PageObjects = new[]
            {
                new PageObjectMapping { SourceType = "LoginPage", TargetType = "LoginPage" }
            },
            Methods = new[]
            {
                new MethodMapping { SourceMethod = "WaitVisible", TargetStatements = new[] { "await Expect({{TARGET}}).ToBeVisibleAsync();" } }
            },
            ParameterizedMethods = new[]
            {
                new ParameterizedMethodMapping { SourceMethodPattern = "WaitForText({text})", TargetStatements = new[] { "await Expect({{TARGET}}).ToHaveTextAsync({text});" } }
            },
            NavigationUrls = new System.Collections.Generic.Dictionary<string, string>
            {
                ["NavigateToMain()"] = "page.GotoAsync(\"/\")"
            },
            SuppressedMethods = new[] { "LegacyCleanup" },
            SuppressedMethodPatterns = new[] { "ObsoleteHelper.*" },
            ScaffoldMethods = new[] { "BillingScaffold" },
            GenericResultMethods = new[] { "GetResult" },
            TargetKnownIdentifiers = new[] { "Navigation" },
            Scopes = new[]
            {
                new ProfileScope
                {
                    Name = "payments",
                    SourcePathPatterns = new[] { "Tests/Payments/**" },
                    Methods = new[] { new MethodMapping { SourceMethod = "ScopeOnlyMethod", TargetStatements = new[] { "x();" } } },
                    NavigationUrls = new System.Collections.Generic.Dictionary<string, string> { ["ScopeNav()"] = "goto()" }
                }
            }
        };
    }

    [Fact]
    public void ExtractRules_CoversAllSections_IncludingScopedAndKeys()
    {
        var rules = ConfigSourceReportBuilder.ExtractRules(SectionsConfig());

        Assert.Contains(rules, r => r.Section == "NavigationUrls" && r.Key == "NavigateToMain()");
        Assert.Contains(rules, r => r.Section == "SuppressedMethods" && r.Key == "LegacyCleanup");
        Assert.Contains(rules, r => r.Section == "SuppressedMethodPatterns" && r.Key == "ObsoleteHelper.*");
        Assert.Contains(rules, r => r.Section == "ScaffoldMethods" && r.Key == "BillingScaffold");
        Assert.Contains(rules, r => r.Section == "GenericResultMethods" && r.Key == "GetResult");
        Assert.Contains(rules, r => r.Section == "PageObjects" && r.Key == "LoginPage");
        Assert.Contains(rules, r => r.Section == "Scopes/payments/Methods" && r.Key == "ScopeOnlyMethod");
        Assert.Contains(rules, r => r.Section == "Scopes/payments/NavigationUrls" && r.Key == "ScopeNav()");
        Assert.Contains(rules, r => r.Section == "ParameterizedMethods" && r.Kind == ConfigSourceKeyKind.Pattern);
        Assert.Contains(rules, r => r.Section == "Methods" && r.Kind == ConfigSourceKeyKind.Expression);
        Assert.Contains(rules, r => r.Section == "TargetKnownIdentifiers" && r.Kind == ConfigSourceKeyKind.Identifier);
    }

    [Fact]
    public void Build_MarksUsedWhenKeyOccursAndUnusedWhenAbsent()
    {
        var config = new ProjectAdapterConfig
        {
            Methods = new[]
            {
                new MethodMapping { SourceMethod = "WaitVisible", TargetStatements = new[] { "x();" } },
                new MethodMapping { SourceMethod = "NeverCalledInSource", TargetStatements = new[] { "y();" } }
            }
        };

        var report = ConfigSourceReportBuilder.Build("corpus", config, SourceFiles());

        var used = report.Usages.Single(u => u.Section == "Methods" && u.Key == "WaitVisible");
        Assert.True(used.Used);
        Assert.Equal(1, used.Occurrences);

        var unused = report.Usages.Single(u => u.Section == "Methods" && u.Key == "NeverCalledInSource");
        Assert.False(unused.Used);
        Assert.Equal(0, unused.Occurrences);
        Assert.Null(unused.ExampleFile);
    }

    [Fact]
    public void Build_ReportsCoveragePercent()
    {
        var config = new ProjectAdapterConfig
        {
            Methods = new[]
            {
                new MethodMapping { SourceMethod = "WaitVisible", TargetStatements = new[] { "x();" } },
                new MethodMapping { SourceMethod = "Ghost", TargetStatements = new[] { "y();" } }
            }
        };

        var report = ConfigSourceReportBuilder.Build("corpus", config, SourceFiles());

        Assert.Equal(2, report.Summary.TotalKeys);
        Assert.Equal(1, report.Summary.UsedKeys);
        Assert.Equal(1, report.Summary.UnusedKeys);
        Assert.Equal(50.0, report.Summary.CoveragePercent);
    }

    [Theory]
    [InlineData("WaitVisible()", "WaitVisible", ConfigSourceKeyKind.Identifier, 1)]
    [InlineData("MyWaitVisible()", "WaitVisible", ConfigSourceKeyKind.Identifier, 0)]
    [InlineData("WaitVisible", "WaitVisible", ConfigSourceKeyKind.Identifier, 1)]
    [InlineData("By.Id(\"login\")", "By.Id(\"login\")", ConfigSourceKeyKind.Expression, 1)]
    [InlineData("WaitForText(\"ok\")", "WaitForText({text})", ConfigSourceKeyKind.Pattern, 1)]
    [InlineData("ObsoleteHelper.Ping()", "ObsoleteHelper.*", ConfigSourceKeyKind.Pattern, 1)]
    [InlineData("ObsoleteHelper1()", "ObsoleteHelper.*", ConfigSourceKeyKind.Pattern, 0)]
    public void CountOccurrences_RespectsKeyKind(string text, string key, ConfigSourceKeyKind kind, int expected)
    {
        var rule = new ConfigSourceRuleInfo("T", key, kind);
        var count = ConfigSourceReportBuilder.CountOccurrences(text, rule, out _);
        Assert.Equal(expected, count);
    }

    [Fact]
    public void CountOccurrences_ReportsFirstLine()
    {
        var text = "line one\nline two WaitVisible\nline three WaitVisible";
        var rule = new ConfigSourceRuleInfo("T", "WaitVisible", ConfigSourceKeyKind.Identifier);
        var count = ConfigSourceReportBuilder.CountOccurrences(text, rule, out var firstLine);

        Assert.Equal(2, count);
        Assert.Equal(2, firstLine);
    }

    [Fact]
    public void Build_EmptyConfig_ProducesZeroCoverageWithoutError()
    {
        var report = ConfigSourceReportBuilder.Build("corpus", new ProjectAdapterConfig(), SourceFiles());
        Assert.Equal(0, report.Summary.TotalKeys);
        Assert.Equal(0.0, report.Summary.CoveragePercent);
        Assert.Empty(report.Usages);
    }
}
