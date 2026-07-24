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
        Assert.Contains("FailOnMultipleMatchingScopes=false", error.Message);
    }

    [Fact]
    public void MultipleMatchingScopes_CanUseExplicitCompatibilityMode()
    {
        var config = CreateScopedConfig(new QualityGatesConfig
        {
            FailOnMultipleMatchingScopes = false
        });
        var adapter = new DefaultProjectAdapter(config);

        Assert.Equal("ScopeA", adapter.GetActiveScope(Path.Combine("src", "CatalogPrincipalsFilter.cs")));
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
