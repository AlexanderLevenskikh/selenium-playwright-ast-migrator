using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;

namespace Migrator.Tests;

public class ResearchAuditRegressionTests
{
    [Fact]
    public void MultipleMatchingScopes_FailByDefault()
    {
        var config = CreateScopedConfig(qualityGates: null);
        var adapter = new DefaultProjectAdapter(config);

        var error = Assert.Throws<ConfigValidationError>(() =>
            adapter.GetActiveScope(Path.Combine("src", "CatalogPrincipalsFilter.cs")));

        Assert.Contains("Multiple profile scopes matched", error.Message);
        Assert.Contains("first-match compatibility has been removed", error.Message);
    }

    [Fact]
    public void MultipleMatchingScopes_CompatibilityModeIsRejected()
    {
        var config = CreateScopedConfig(new QualityGatesConfig
        {
            FailOnMultipleMatchingScopes = false
        });

        var error = Assert.Throws<ConfigValidationError>(() => new DefaultProjectAdapter(config));

        Assert.Contains("FailOnMultipleMatchingScopes=false is no longer supported", error.Message);
    }

    [Theory]
    [InlineData("**/*.cs", "C:/repo/Tests/LoginTests.cs")]
    [InlineData("**/DiscountsTests/**", "C:/repo/Features/DiscountsTests/Cases/Smoke.cs")]
    [InlineData("Tests/*.cs", "C:/repo/Tests/LoginTests.cs")]
    [InlineData("Login*.cs", "C:/repo/Tests/LoginTests.cs")]
    public void ScopeResolver_ImplementsDocumentedGlobSemantics(string pattern, string path)
    {
        Assert.True(ScopeResolver.IsMatch(pattern, path));
    }

    [Fact]
    public void TargetPrefixResolution_UsesMostSpecificMappingRegardlessOfConfigOrder()
    {
        static DefaultProjectAdapter Create(params UiTargetMapping[] mappings) => new(new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            UiTargets = mappings
        });

        var broad = new UiTargetMapping("page.Panel", "broad", "TestId");
        var specific = new UiTargetMapping("page.Panel.Button", "specific", "TestId");

        var first = Assert.IsType<MappedTarget>(Create(broad, specific).ResolveTarget("page.Panel.Button.Icon"));
        var second = Assert.IsType<MappedTarget>(Create(specific, broad).ResolveTarget("page.Panel.Button.Icon"));

        Assert.Equal("specific", first.TargetExpression);
        Assert.Equal(first.TargetExpression, second.TargetExpression);
    }

    [Fact]
    public void ConfigValidator_RejectsDuplicateMappingKeys()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            UiTargets = new[]
            {
                new UiTargetMapping("page.Submit", "submit", "TestId"),
                new UiTargetMapping("page.Submit", "submit-duplicate", "TestId")
            },
            Methods = new[]
            {
                new MethodMapping("Navigation.Open", "OpenAsync", null, null, false),
                new MethodMapping("Navigation.Open", "OpenAgainAsync", null, null, false)
            },
            ParameterizedMethods = new[]
            {
                new ParameterizedMethodMapping("Helper.Run({value})", new[] { "await RunAsync({value});" }, false),
                new ParameterizedMethodMapping("Helper.Run({value})", new[] { "await RunAgainAsync({value});" }, false)
            }
        };

        var error = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        Assert.Contains("UiTargets[1].SourceExpression duplicates UiTargets[0].SourceExpression", error.Message);
        Assert.Contains("Methods[1].SourceMethod duplicates Methods[0].SourceMethod", error.Message);
        Assert.Contains("ParameterizedMethods[1].SourceMethodPattern duplicates", error.Message);
    }

    [Fact]
    public void ExactMethodSignature_SupportsNestedGenericsTupleAttributesAndDefaults()
    {
        var output = Migrate("""
namespace Sample.Tests;
public class ComplexSignatureTests
{
    [Test]
    public void Builds()
    {
        var result = await Build<Dictionary<string, List<int>>>(item, cancellationToken);
    }
}
""", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            Methods = new[]
            {
                new MethodMapping(
                    "Build<Dictionary<string, List<int>>>([FromConfig] (int id, string name) item, CancellationToken cancellationToken = default)",
                    targetMethod: null,
                    description: null,
                    targetStatements: new[]
                    {
                        "var {result} = await Target.BuildAsync({item}, {cancellationToken});"
                    },
                    requiresReview: false)
            },
            TargetKnownIdentifiers = new[] { "Target", "item", "cancellationToken" },
            TargetKnownTypes = Array.Empty<string>()
        });

        Assert.Contains("var result = await Target.BuildAsync(item, cancellationToken);", output);
        Assert.DoesNotContain("HELPER_METHOD_REQUIRES_MAPPING", output);
        Assert.DoesNotContain("UNRESOLVED_PLACEHOLDER", output);
    }

    [Fact]
    public void ReportBuilder_DoesNotCountSuppressionOnlyTestAsSuccessfullyConverted()
    {
        var model = new TestFileModel(
            FilePath: "Suppressed.cs",
            Namespace: "Sample.Tests",
            ClassName: "SuppressedTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "WaitOnly",
                    null,
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    new TestAction[] { new RawStatementAction(12, "page.WaitLoaded()") })
            })
        {
            SuppressedMethodPatterns = new[] { "*page.WaitLoaded(*)" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);
        var report = ReportBuilder.Build(model, output);

        Assert.Contains("EMPTY_TEST_AFTER_SUPPRESSION", output);
        Assert.Equal(0, report.SuccessfullyConvertedTests);
    }

    [Fact]
    public void Mig01_AlreadyMigratedPlaywrightFixture_IsBlockedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig01-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "LoginTestsPlaywright.cs");
        File.WriteAllText(
            source,
            """
            using Microsoft.Playwright.NUnit;

            namespace Sample.Tests;

            public class LoginTestsPlaywright : PageTest
            {
                public async Task Login()
                {
                    await Page.GotoAsync("https://example.test");
                }
            }
            """);

        try
        {
            var parser = new RoslynTestFileParser(new ProjectAdapterConfig
            {
                SourceProjectName = "sample"
            });

            var error = Assert.Throws<SourceInputBlockedException>(() => parser.ParseDirectory(root));

            Assert.Equal(SourceInputBlockedException.StableCode, error.Code);
            Assert.Contains(SourceInputBlockedException.AlreadyMigratedReason, error.Message, StringComparison.Ordinal);
            Assert.Equal(source, error.FilePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mig02_SourceActionsCannotDisappearIntoVacuumTarget()
    {
        var source = new TestFileModel(
            FilePath: "Vacuum.cs",
            Namespace: "Sample.Tests",
            ClassName: "VacuumTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "KeepsBehavior",
                    null,
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    new TestAction[]
                    {
                        new RawStatementAction(10, "page.Submit.Click();")
                    })
            });

        var target = new TestFileModel(
            FilePath: "Vacuum.cs",
            Namespace: "Sample.Tests",
            ClassName: "VacuumTestsPlaywright",
            BaseClassName: "PageTest",
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "KeepsBehavior",
                    null,
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    Array.Empty<TestAction>())
            });

        const string generated = """
            using Microsoft.Playwright.NUnit;
            namespace Sample.Tests;
            public class VacuumTestsPlaywright : PageTest
            {
                public async Task KeepsBehavior()
                {
                }
            }
            """;

        var pipelineResult = new PipelineResult(
            source,
            target,
            generated,
            ReportBuilder.Build(target, generated));

        var report = VerifyRunner.Run(
            new[] { pipelineResult },
            config: null);

        Assert.Equal("failed", report.Status);
        var issue = Assert.Single(report.Issues.Where(item => item.Category == "VacuumTest"));
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("no active migrated actions", issue.Message, StringComparison.Ordinal);
    }

    static ProjectAdapterConfig CreateScopedConfig(QualityGatesConfig? qualityGates)
    {
        return new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            QualityGates = qualityGates,
            Scopes = new[]
            {
                new ProfileScope("ScopeA", new[] { "CatalogPrincipalsFilter.cs" }),
                new ProfileScope("ScopeB", new[] { "**/CatalogPrincipalsFilter.cs" })
            }
        };
    }

    static string Migrate(string source, ProjectAdapterConfig config)
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-research-audit-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, source);
        try
        {
            var parsed = new RoslynTestFileParser(config).Parse(file);
            var adapted = new DefaultProjectAdapter(config).Adapt(parsed);
            return new PlaywrightDotNetRenderer().Render(adapted);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
