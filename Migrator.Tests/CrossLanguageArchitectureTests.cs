using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.Core.Profiles;
using Migrator.Core.SourceFrontends;
using Migrator.PlaywrightDotNet;
using Migrator.PlaywrightTypeScript;
using Migrator.Roslyn;

namespace Migrator.Tests;

public class CrossLanguageArchitectureTests
{
    [Fact]
    public void LegacyIrBridge_ProducesMigrationDocumentAndCanLowerBack()
    {
        var model = new TestFileModel(
            FilePath: "/tmp/SampleTests.cs",
            Namespace: "Sample.Tests",
            ClassName: "SampleTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "ClicksAndAsserts",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("save", "#save", TargetKind.CssSelector)),
                        new SendKeysAction(11, TargetExpression.Mapped("name", "#name", TargetKind.CssSelector), "\"Alex\""),
                        new TextAssertionAction(12, TargetExpression.Mapped("status", ".status", TargetKind.CssSelector), TextAssertionKind.TextEquals, "\"Saved\"")
                    })
            });

        var document = LegacyIrBridge.ToDocument(model);

        Assert.Equal("selenium-csharp", document.Source.Id);
        Assert.Equal("SampleTests", document.Suite.ClassName);
        Assert.IsType<ClickStatementIr>(document.Suite.Tests.Single().Body[0]);
        Assert.IsType<FillStatementIr>(document.Suite.Tests.Single().Body[1]);
        Assert.IsType<AssertionStatementIr>(document.Suite.Tests.Single().Body[2]);

        var lowered = LegacyIrBridge.ToLegacyTestFile(document);
        Assert.Equal(model.ClassName, lowered.ClassName);
        Assert.Equal(3, lowered.Tests.Single().BodyActions.Count());
    }

    [Fact]
    public void TargetBackend_CanRenderIrDocumentThroughCompatibilityBridge()
    {
        var model = new TestFileModel(
            FilePath: "/tmp/SampleTests.cs",
            Namespace: "Sample.Tests",
            ClassName: "SampleTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(3, TargetExpression.Mapped("save", "#save", TargetKind.CssSelector))
                    })
            });
        var document = LegacyIrBridge.ToDocument(model, target: new PlaywrightDotNetBackend().Target);

        var rendered = new PlaywrightDotNetBackend().RenderDocument(document);

        Assert.Contains("await Page.Locator(\"#save\").ClickAsync();", rendered);
    }

    [Fact]
    public void SourceFrontendRegistry_ResolvesCSharpAndJavaFrontends()
    {
        var registry = new SourceFrontendRegistry()
            .Register(new CSharpSeleniumFrontend())
            .Register(new JavaSeleniumFrontend());

        Assert.Equal("selenium-csharp", registry.Resolve("csharp-selenium").Source.Id);
        Assert.Equal("selenium-java", registry.Resolve("java-selenium").Source.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("ruby-selenium"));
        Assert.Contains("Unknown source frontend 'ruby-selenium'", ex.Message);
        Assert.Contains("selenium-csharp", ex.Message);
        Assert.Contains("selenium-java", ex.Message);
    }

    [Fact]
    public void ConfigNormalizer_SplitsLegacyConfigIntoSourceTargetProjectProfiles()
    {
        var config = new ProjectAdapterConfig(
            "Sample",
            new[] { new UiTargetMapping("page.Save", "save", "TestIdBeginning") },
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping(
                    "page.Save.WaitVisible()",
                    targetMethod: null,
                    description: null,
                    targetStatements: new[] { "await Assertions.Expect({TARGET}).ToBeVisibleAsync();" },
                    requiresReview: false)
            },
            SourceOnlyIdentifiers: new[] { "Urls" },
            TargetKnownTypes: new[] { "Navigation" });

        var result = ProjectAdapterConfigNormalizer.Normalize(
            config,
            source: CSharpSeleniumFrontend.Spec,
            target: new PlaywrightDotNetBackend().Target);

        Assert.Equal("selenium-csharp", result.Profile.Source.Source.Id);
        Assert.Equal("playwright-dotnet", result.Profile.Target.Target.Id);
        Assert.Contains("Urls", result.Profile.Source.SourceOnlyIdentifiers);
        Assert.Contains("Navigation", result.Profile.Target.TargetKnownTypes);
        Assert.Single(result.Profile.Project.UiTargets);
        Assert.Contains(result.Warnings, w => w.Code == "CONFIG_V1_LEGACY_TARGET_STATEMENTS");
    }

    [Fact]
    public void ConfigValidator_ReturnsNonFatalMigrationWarnings()
    {
        var config = new ProjectAdapterConfig(
            "Sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping("Helper.Do()", null, null, new[] { "await Helper.DoAsync();" }, false)
            });

        var warnings = ConfigValidator.GetMigrationWarnings(config);

        Assert.Contains(warnings, w => w.Path == "Methods[0].TargetStatements");
    }

    [Fact]
    public void JavaSeleniumParser_RecognizesCommonWebDriverActions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "java-selenium-spike-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "LoginTest.java");
            File.WriteAllText(file, """
                package sample.tests;

                import org.junit.jupiter.api.Test;
                import static org.junit.jupiter.api.Assertions.*;

                public class LoginTest {
                    @Test
                    public void loginWorks() {
                        driver.findElement(By.id("user")).sendKeys("alex");
                        driver.findElement(By.cssSelector("#save")).click();
                        assertEquals("Saved", driver.findElement(By.cssSelector(".status")).getText());
                        assertTrue(driver.findElement(By.id("done")).isDisplayed());
                    }
                }
                """);

            var model = new JavaSeleniumTestFileParser().Parse(file);

            Assert.Equal("sample.tests", model.Namespace);
            Assert.Equal("LoginTest", model.ClassName);
            var actions = model.Tests.Single().BodyActions.ToArray();
            Assert.IsType<SendKeysAction>(actions[0]);
            Assert.IsType<ClickAction>(actions[1]);
            Assert.IsType<TextAssertionAction>(actions[2]);
            Assert.IsType<VisibilityAssertionAction>(actions[3]);

            var ts = new PlaywrightTypeScriptBackend().Render(model);
            Assert.Contains("await page.locator('#save').click();", ts);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void JavaSeleniumFrontend_ParsesToIrDocuments()
    {
        var dir = Path.Combine(Path.GetTempPath(), "java-selenium-frontend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "SmokeTest.java");
            File.WriteAllText(file, """
                public class SmokeTest {
                    @Test
                    public void smoke() {
                        driver.findElement(By.id("save")).click();
                    }
                }
                """);

            var frontend = new JavaSeleniumFrontend();
            var result = frontend.Parse(new MigrationRequest(
                JavaSeleniumFrontend.Spec,
                new PlaywrightTypeScriptBackend().Target,
                file));

            var document = Assert.Single(result.Documents);
            Assert.Equal("selenium-java", document.Source.Id);
            Assert.IsType<ClickStatementIr>(document.Suite.Tests.Single().Body.Single());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
