using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Xunit;

namespace Migrator.Tests;

public sealed class MigrationScaffoldingProtocolTests
{
    [Fact]
    public void ExplicitQualifiedScaffold_PreservesAwaitedCallShapeAndCompiles()
    {
        var action = new MethodInvocationAction(
            12,
            "TariffSettingsHelper",
            "FindTariff",
            "await TariffSettingsHelper.FindTariff(name)",
            new[] { "name" },
            Array.Empty<string>(),
            "tariffModel",
            RecognitionConfidence.SyntaxFallback,
            isAwaited: true);
        var model = CreateModel(action) with
        {
            ScaffoldMethodPatterns = new[] { "TariffSettingsHelper.*" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("dynamic tariffModel = await __MigratorScaffoldRuntime.InvokeAsync", output, StringComparison.Ordinal);
        Assert.Contains("\"TariffSettingsHelper.FindTariff\"", output, StringComparison.Ordinal);
        Assert.Contains("[MIGRATOR:SCAFFOLD]", output, StringComparison.Ordinal);
        Assert.Contains("NotImplementedException", output, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO:", output, StringComparison.Ordinal);
        Assert.True(CompileChecker.CompilesWithoutErrors(output), CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void ScaffoldRuntime_IsNestedSoMultipleGeneratedFilesCompileTogether()
    {
        static MethodInvocationAction CreateAction() => new(
            12,
            "TariffSettingsHelper",
            "FindTariff",
            "await TariffSettingsHelper.FindTariff(name)",
            new[] { "name" },
            Array.Empty<string>(),
            "tariffModel",
            RecognitionConfidence.SyntaxFallback,
            isAwaited: true);

        var first = CreateModel(CreateAction()) with
        {
            FilePath = "FirstTariffTests.cs",
            ClassName = "FirstTariffTests",
            ScaffoldMethodPatterns = new[] { "TariffSettingsHelper.*" }
        };
        var second = CreateModel(CreateAction()) with
        {
            FilePath = "SecondTariffTests.cs",
            ClassName = "SecondTariffTests",
            ScaffoldMethodPatterns = new[] { "TariffSettingsHelper.*" }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var firstOutput = renderer.Render(first);
        var secondOutput = renderer.Render(second);

        Assert.Contains("private static class __MigratorScaffoldRuntime", firstOutput, StringComparison.Ordinal);
        Assert.Contains("private static class __MigratorScaffoldRuntime", secondOutput, StringComparison.Ordinal);
        Assert.True(CompileChecker.CompilesWithoutErrors(firstOutput, secondOutput),
            CompileChecker.FormatErrors(firstOutput, secondOutput));
    }

    [Fact]
    public void UnknownProjectHelper_StaysVisibleUntilAnExplicitScaffoldDecisionExists()
    {
        var action = new MethodInvocationAction(
            8,
            "TariffSettingsHelper",
            "FindTariff",
            "TariffSettingsHelper.FindTariff(name)",
            new[] { "name" },
            Array.Empty<string>(),
            "tariffModel",
            RecognitionConfidence.SyntaxFallback);

        var output = new PlaywrightDotNetRenderer().Render(CreateModel(action));

        Assert.DoesNotContain("__MigratorScaffoldRuntime", output, StringComparison.Ordinal);
        Assert.Contains("[MIGRATOR:HELPER_METHOD_REQUIRES_MAPPING]", output, StringComparison.Ordinal);
        Assert.Contains("TariffSettingsHelper.FindTariff", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BareScaffoldMethod_DoesNotMatchEveryQualifiedReceiver()
    {
        var action = new MethodInvocationAction(
            8,
            "TariffSettingsHelper",
            "FindTariff",
            "TariffSettingsHelper.FindTariff(name)",
            new[] { "name" },
            Array.Empty<string>(),
            "tariffModel",
            RecognitionConfidence.SyntaxFallback);
        var model = CreateModel(action) with
        {
            ScaffoldMethods = new[] { "FindTariff" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.DoesNotContain("__MigratorScaffoldRuntime", output, StringComparison.Ordinal);
        Assert.Contains("[MIGRATOR:HELPER_METHOD_REQUIRES_MAPPING]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Suppression_RemainsDifferentFromScaffolding()
    {
        var action = new MethodInvocationAction(
            8,
            "TariffSettingsHelper",
            "FindTariff",
            "TariffSettingsHelper.FindTariff(name)",
            new[] { "name" },
            RecognitionConfidence.SyntaxFallback);
        var model = CreateModel(action) with
        {
            SuppressedMethodPatterns = new[] { "TariffSettingsHelper.*" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.DoesNotContain("__MigratorScaffoldRuntime", output, StringComparison.Ordinal);
        Assert.Contains("suppressed", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*.FindTariff")]
    [InlineData("TariffSettingsHelper")]
    [InlineData("TariffSettingsHelper.Select?")]
    public void ConfigValidator_RejectsBroadScaffoldPatterns(string pattern)
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "Sample",
            ScaffoldMethodPatterns = new[] { pattern }
        };

        var error = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        Assert.Contains(error.Errors, item => item.Contains("too broad", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void ConfigValidator_AcceptsNarrowQualifiedScaffoldPattern()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "Sample",
            ScaffoldMethodPatterns = new[] { "TariffSettingsHelper.Select*" }
        };

        ConfigValidator.Validate(config);
    }

    [Fact]
    public void ConfigValidator_RejectsCrossOverlapBetweenSuppressionAndScaffolding()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "Sample",
            SuppressedMethodPatterns = new[] { "TariffSettingsHelper.*" },
            ScaffoldMethods = new[] { "TariffSettingsHelper.FindTariff" }
        };

        var error = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        Assert.Contains(error.Errors, item => item.Contains("cannot both own", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfigValidator_RejectsTheSameRootAsSuppressedAndScaffolded()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "Sample",
            SuppressedMethodPatterns = new[] { "TariffSettingsHelper.*" },
            ScaffoldMethodPatterns = new[] { "TariffSettingsHelper.*" }
        };

        var error = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        Assert.Contains(error.Errors, item => item.Contains("cannot both own", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_PreservesAwaitedProjectHelperInvocation()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-awaited-helper-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, """
using System.Threading.Tasks;
using NUnit.Framework;
namespace Sample;
public class TariffTests
{
    [Test]
    public async Task Works()
    {
        await TariffSettingsHelper.SelectTariffMarksAsync();
    }
}
""");

        try
        {
            var model = new RoslynTestFileParser().Parse(file);
            var action = Assert.IsType<MethodInvocationAction>(Assert.Single(Assert.Single(model.Tests).BodyActions));

            Assert.True(action.IsAwaited);
            Assert.Equal("TariffSettingsHelper", action.ReceiverExpression);
            Assert.Equal("SelectTariffMarksAsync", action.MethodName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    static TestFileModel CreateModel(params TestAction[] actions) => new(
        FilePath: "TariffTests.cs",
        Namespace: "Generated.Tests",
        ClassName: "TariffTests",
        BaseClassName: null,
        SetUpActions: Array.Empty<TestAction>(),
        Tests: new[]
        {
            new TestModel(
                "Works",
                null,
                Array.Empty<TestCaseData>(),
                Array.Empty<MethodParameterModel>(),
                actions)
        });
}
