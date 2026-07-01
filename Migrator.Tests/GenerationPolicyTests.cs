using Migrator.Core;
using Migrator.SeleniumCSharp;
using Xunit;

namespace Migrator.Tests;

public class GenerationPolicyTests
{
    [Fact]
    public void ConfigValidator_AcceptsKnownGenerationPoliciesAndRejectsTypos()
    {
        ConfigValidator.Validate(new ProjectAdapterConfig { GenerationPolicy = "conservative" });
        ConfigValidator.Validate(new ProjectAdapterConfig { GenerationPolicy = "balanced" });
        ConfigValidator.Validate(new ProjectAdapterConfig { GenerationPolicy = "aggressive" });

        var error = Assert.Throws<ConfigValidationError>(() =>
            ConfigValidator.Validate(new ProjectAdapterConfig { GenerationPolicy = "reckless" }));
        Assert.Contains(error.Errors, x => x.Contains("GenerationPolicy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerationPolicy_RewritesMappedHelperReviewFlagsWithoutInventingSelectors()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "demo",
            UiTargets = new[] { new UiTargetMapping("page.Save", "save", "TestId") },
            Methods = new[]
            {
                new MethodMapping("page.SaveAndWait()", null, "safe helper", new[] { "await Page.GetByTestId(\"save\").ClickAsync();" }, false)
            },
            ParameterizedMethods = new[]
            {
                new ParameterizedMethodMapping("page.Select({value})", new[] { "await Page.GetByText({value}).ClickAsync();" }, false, "select helper")
            }
        };

        var conservative = GenerationPolicy.Apply(config, "conservative");
        Assert.Equal("conservative", conservative.GenerationPolicy);
        Assert.All(conservative.Methods, m => Assert.True(m.RequiresReview));
        Assert.All(conservative.ParameterizedMethods, m => Assert.True(m.RequiresReview));
        Assert.Equal(config.UiTargets[0].TargetExpression, conservative.UiTargets[0].TargetExpression);

        var aggressive = GenerationPolicy.Apply(config, "aggressive");
        Assert.Equal("aggressive", aggressive.GenerationPolicy);
        Assert.All(aggressive.Methods, m => Assert.False(m.RequiresReview));
        Assert.All(aggressive.ParameterizedMethods, m => Assert.False(m.RequiresReview));
        Assert.Equal(config.UiTargets[0].TargetExpression, aggressive.UiTargets[0].TargetExpression);
    }

    [Fact]
    public void Program_WiresGenerationPolicyThroughCliConfigAndReports()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var reportWriter = File.ReadAllText(FindRepositoryFile("Migrator.Core/ReportWriter.cs"));
        var schema = File.ReadAllText(FindRepositoryFile("schemas/adapter-config.schema.json"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/generation-policy.md"));

        Assert.Contains("--generation-policy", program);
        Assert.Contains("GenerationPolicy.Apply", program);
        Assert.Contains("conservative|balanced|aggressive", catalog);
        Assert.Contains("Generation policy", reportWriter);
        Assert.Contains("GenerationPolicy", schema);
        Assert.Contains("never invents selectors", docs);
        Assert.Contains("aggressive", docs);
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
