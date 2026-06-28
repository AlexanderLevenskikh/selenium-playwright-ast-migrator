using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.PlaywrightTypeScript;

namespace Migrator.Tests;

/// <summary>
/// PROD-13 locks the TypeScript test-host layer separately from action rendering:
/// imports, optional describe wrappers, custom fixture parameters, and locator
/// declarations should be stable in both legacy and IR V2 paths.
/// </summary>
public sealed class TypeScriptTestHostRenderingTests
{
    [Fact]
    public void DefaultHost_PreservesLegacyShape_InBothPaths()
    {
        var (legacy, irV2) = RenderBoth(new PlaywrightTypeScriptRenderOptions());

        foreach (var output in new[] { legacy, irV2 })
        {
            Assert.Contains("import { test, expect } from '@playwright/test';", output);
            Assert.Contains("test('HostCase', async ({ page }) => {", output);
            Assert.DoesNotContain("test.describe", output);
            Assert.Contains("const save = page.locator('#save');", output);
            Assert.Contains("await save.click();", output);
        }
    }

    [Fact]
    public void CustomHost_DeduplicatesImports_UsesDescribeAndFixtureParameter_InBothPaths()
    {
        var options = new PlaywrightTypeScriptRenderOptions
        {
            ImportLines = new[]
            {
                "import { authTest as test, expect } from '../fixtures/auth';",
                "import { LoginPage } from '../pages/LoginPage';",
                "import { LoginPage } from '../pages/LoginPage';"
            },
            TestFunctionName = "test",
            FixtureParameter = "{ page, loginPage }",
            UseDescribe = true,
            DescribeName = "Generated auth suite"
        };

        var (legacy, irV2) = RenderBoth(options);

        foreach (var output in new[] { legacy, irV2 })
        {
            Assert.Equal(1, CountOccurrences(output, "import { LoginPage } from '../pages/LoginPage';"));
            Assert.Contains("import { authTest as test, expect } from '../fixtures/auth';", output);
            Assert.Contains("test.describe('Generated auth suite', () => {", output);
            Assert.Contains("  test('HostCase', async ({ page, loginPage }) => {", output);
            Assert.Contains("    const save = page.locator('#save');", output);
            Assert.Contains("    await save.click();", output);
        }
    }

    [Fact]
    public void CustomTestFunctionName_IsUsedForDescribeAndTests_InBothPaths()
    {
        var options = new PlaywrightTypeScriptRenderOptions
        {
            ImportLines = new[] { "import { authTest, expect } from '../fixtures/auth';" },
            TestFunctionName = "authTest",
            UseDescribe = true,
            DescribeName = "Auth suite"
        };

        var (legacy, irV2) = RenderBoth(options);

        foreach (var output in new[] { legacy, irV2 })
        {
            Assert.Contains("authTest.describe('Auth suite', () => {", output);
            Assert.Contains("  authTest('HostCase', async ({ page }) => {", output);
            Assert.DoesNotContain("test('HostCase'", output);
        }
    }

    static (string Legacy, string IrV2) RenderBoth(PlaywrightTypeScriptRenderOptions options)
    {
        var backend = new PlaywrightTypeScriptBackend(options);
        var model = new TestFileModel(
            FilePath: "/repo/HostTests.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "HostTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "HostCase",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new LocatorDeclarationAction(10, "save", "#save", "var save = Driver.FindElement(By.CssSelector(\"#save\"));"),
                        new ClickAction(11, TargetExpression.Mapped("save", "save", TargetKind.RawExpression))
                    })
            });

        var document = LegacyIrBridge.ToDocument(model, target: backend.Target);
        return (Normalize(backend.Render(model)), Normalize(backend.RenderDocument(document)));
    }

    static int CountOccurrences(string text, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
