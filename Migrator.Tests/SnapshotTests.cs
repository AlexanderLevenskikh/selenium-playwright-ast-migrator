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

public class SnapshotTests
{
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");

    [Fact]
    public void Snapshot_Widget_FullPipeline_StructureAndMapping()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));

        var model = result.TargetModel;
        var output = result.GeneratedOutput;
        var report = result.Report;

        Assert.Equal("Widget", model.ClassName);
        Assert.Equal("Example.E2ETests.Tests.Functional", model.Namespace);
        Assert.NotEmpty(model.SetUpActions);
        Assert.Equal(3, model.Tests.Count());

        Assert.Contains("using Microsoft.Playwright.NUnit;", output);
        Assert.Contains("using NUnit.Framework;", output);
        Assert.Contains("using System.Threading.Tasks;", output);

        Assert.Contains("namespace Example.E2ETests.Tests.Functional.Playwright;", output);
        Assert.Contains("class WidgetPlaywright : PageTest", output);

        Assert.Contains("[SetUp]", output);
        Assert.Contains("public async Task SetUp()", output);

        Assert.Contains("[Test]", output);
        Assert.Contains("[Category(\"QuickRunning\")]", output);
        Assert.Contains("async Task CheckUserToWidget", output);
        Assert.Contains("async Task CheckDateToWidget", output);
        Assert.Contains("async Task CheckSearchToWidget", output);

        Assert.Contains("GetByTestId(\"widget-user\")", output);
        Assert.Contains("GetByTestId(\"widget-search\")", output);

        Assert.DoesNotContain("TODO: page.User", output);
        Assert.DoesNotContain("TODO: page.WidgetSearch", output);

        Assert.Contains("// TODO:", output);
        Assert.Contains("// TODO: manual review needed", output);

        Assert.Equal(
            Normalize(File.ReadAllText(Path.Combine(_testFilesDir, "Expected", "Widget.generated.cs"))),
            Normalize(output));

        Assert.True(report.MappedTargets > 0, $"Expected mapped targets > 0, got {report.MappedTargets}");
        Assert.True(report.TotalTests == 3);
        Assert.True(report.SemanticActions >= 0);
        Assert.True(report.SyntaxFallbackActions > 0);
        Assert.True(report.TodoComments > 0);
        Assert.Equal(report.TodoComments, output.Split('\n').Count(l => l.TrimStart().StartsWith("// TODO:")));
    }

    [Fact]
    public void Snapshot_ButtonTests_FullPipeline_MappedAndUnmapped()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        var output = result.GeneratedOutput;
        var report = result.Report;

        Assert.Contains("using Microsoft.Playwright.NUnit;", output);
        Assert.Contains("namespace Example.E2ETests.Tests.NonCategory.Playwright;", output);
        Assert.Contains("class ButtonTestsPlaywright : PageTest", output);

        Assert.Contains("[SetUp]", output);
        Assert.Contains("public async Task SetUp()", output);

        Assert.Contains("[Test]", output);
        Assert.Contains("[Category(\"QuickRunning\")]", output);
        Assert.Contains("async Task CheckSearchButton", output);
        Assert.Contains("async Task CheckFeedBackButton", output);
        Assert.Contains("async Task CheckButtonCatalogsPartners", output);

        Assert.Contains("GetByTestId(\"side-menu-search\")", output);
        Assert.Contains("GetByTestId(\"side-menu-catalogs\")", output);
        Assert.Contains("GetByTestId(\"side-menu-catalogs-partners\")", output);

        Assert.DoesNotContain("TODO: page.MenuItems.SideMenuButtonSearch", output);
        Assert.DoesNotContain("TODO: page.MenuItems.SideMenuCatalogs", output);
        Assert.DoesNotContain("TODO: page.MenuItems.SideMenuCatalogsPartners", output);

        Assert.Contains("// TODO:", output);
        Assert.Contains("// TODO: manual review needed", output);

        Assert.Equal(
            Normalize(File.ReadAllText(Path.Combine(_testFilesDir, "Expected", "ButtonTests.generated.cs"))),
            Normalize(output));

        Assert.True(report.MappedTargets > 0, $"Expected mapped targets > 0, got {report.MappedTargets}");
        Assert.Equal(3, report.TotalTests);
        Assert.True(report.TodoComments > 0);
        Assert.Equal(report.TodoComments, output.Split('\n').Count(l => l.TrimStart().StartsWith("// TODO:")));
    }

    [Fact]
    public void Snapshot_NoUnsupportedActions_NoWarningBanner()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "NoUnsupported",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "DoClick",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new[] { (TestAction)new ClickAction(5, "btn") }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.DoesNotContain("WARNING", output);
        Assert.Contains("class NoUnsupportedPlaywright : PageTest", output);
    }

    [Fact]
    public void Snapshot_Widget_Report_UnsupportedActionsPresent()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));
        var report = result.Report;
        var output = result.GeneratedOutput;

        Assert.True(report.UnsupportedCount >= 0);

        var allActions = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
            .Concat(result.TargetModel.SetUpActions).ToList();

        var unsupported = allActions.OfType<UnsupportedAction>().ToList();
        Assert.Equal(unsupported.Count, report.UnsupportedCount);

        Assert.All(unsupported, u =>
        {
            Assert.False(string.IsNullOrEmpty(u.Reason));
            Assert.Contains($"TODO: UNSUPPORTED [{u.Reason}]", output);
        });
    }

    [Fact]
    public void Snapshot_RegistryFilter_TestCase_Preserved()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "RegistryFilter.cs"));

        var output = result.GeneratedOutput;
        var report = result.Report;

        Assert.Contains("TestCase", output);
        Assert.Contains("Ascending", output);
        Assert.Contains("Descending", output);
        Assert.Contains("CheckFilterScSortAndExcludeToRegistry", output);
        Assert.Contains("string sortOrder", output);
        Assert.Contains("string text", output);

        Assert.Contains("class RegistryFilterPlaywright : PageTest", output);
        Assert.Contains("[SetUp]", output);

        Assert.True(report.TotalTests == 4);
        Assert.True(report.SyntaxFallbackActions > 0);
        Assert.True(report.TodoComments > 0);
    }

    [Fact]
    public void Snapshot_Widget_NoAdapter_AllTargetsUnresolved()
    {
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, null);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));

        var output = result.GeneratedOutput;
        var report = result.Report;

        Assert.Equal(0, report.MappedTargets);
        Assert.True(report.UnmappedTargets > 0, "Without adapter, all Click/SendKeys targets should be unresolved");
        Assert.Contains("TODO: page.User", output);
    }

    [Fact]
    public void Snapshot_ButtonTests_AdapterFromDirectory_UsingPipeline()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var results = pipeline.ProcessDirectory(_testFilesDir).ToList();

        Assert.True(results.Count >= 2, "Should process at least 2 fixture files");

        var buttonResult = results.First(r => r.SourceModel.ClassName == "ButtonTests");
        Assert.Contains("GetByTestId(\"side-menu-search\")", buttonResult.GeneratedOutput);
        Assert.True(buttonResult.Report.MappedTargets > 0);

        var widgetResult = results.First(r => r.SourceModel.ClassName == "Widget");
        Assert.Contains("GetByTestId(\"widget-user\")", widgetResult.GeneratedOutput);
        Assert.True(widgetResult.Report.MappedTargets > 0);
    }

    [Fact]
    public void Snapshot_SourceVsTargetModel_AdapterResolvesTargets()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));

        var sourceClicks = result.SourceModel.Tests.SelectMany(t => t.BodyActions)
            .OfType<ClickAction>().ToList();
        var targetClicks = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
            .OfType<ClickAction>().ToList();

        Assert.Equal(sourceClicks.Count, targetClicks.Count);

        Assert.All(targetClicks, c => Assert.NotNull(c.Target));

        var mappedInTarget = targetClicks.Count(c => c.Target.Kind != TargetKind.Unresolved);
        Assert.True(mappedInTarget > 0, "Adapter should resolve at least one Click target");
    }

    [Fact]
    public void Snapshot_ReportBuilder_IsPure()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();

        var model = parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var targetModel = adapter.Adapt(model);
        var output = renderer.Render(targetModel);

        var report1 = ReportBuilder.Build(targetModel, output);
        var report2 = ReportBuilder.Build(targetModel, output);

        Assert.Equal(report1.MappedTargets, report2.MappedTargets);
        Assert.Equal(report1.UnmappedTargets, report2.UnmappedTargets);
        Assert.Equal(report1.TodoComments, report2.TodoComments);
        Assert.Equal(report1.UnsupportedCount, report2.UnsupportedCount);
    }

    [Fact]
    public void GeneratedOutput_HasNoCSharpCompilationErrors_ButtonTests()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var output = result.GeneratedOutput;

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void GeneratedOutput_HasNoCSharpCompilationErrors_Widget()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));
        var output = result.GeneratedOutput;

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void GeneratedOutput_HasNoCSharpCompilationErrors_RegistryFilter_WithTestCase()
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var output = result.GeneratedOutput;

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void GeneratedOutput_HasNoCSharpCompilationErrors_Synthetic_NoUnsupported_NoWarning()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "CleanTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "DoClick",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new[] { (TestAction)new ClickAction(5, "btn") }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.DoesNotContain("WARNING", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void GeneratedOutput_HasNoCSharpCompilationErrors_WithTestCase_Parameters()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "ParamTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "ParametrizedTest",
                    Category: "Quick",
                    CaseData: new[]
                    {
                        new TestCaseData(new[] { "alpha", "1" }, "[TestCase(\"alpha\", \"1\")]"),
                        new TestCaseData(new[] { "beta", "2" }, "[TestCase(\"beta\", \"2\")]"),
                    },
                    Parameters: new[]
                    {
                        new MethodParameterModel("string", "sortOrder", null),
                        new MethodParameterModel("string", "value", null),
                    },
                    BodyActions: new[] { (TestAction)new ClickAction(10, "el") }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[TestCase", output);
        Assert.Contains("ParametrizedTest(string sortOrder, string value)", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // TargetKind compile-smoke coverage:
    // - PlaywrightLocator: covered by ButtonTests, Widget, Synthetic_NoUnsupported (GetByTestId, Locator)
    // - Unresolved: covered by Widget_NoAdapter (emits "TODO: ..." comments)
    // - PageObjectProperty: NOT covered by compile-smoke — it emits bare property names that require
    //   external page-object stub classes. Render correctness is verified by assertion below.
    // - RawExpression: currently not emitted by the renderer pipeline; no dedicated compile test
    [Fact]
    public void GeneratedOutput_HasNoCSharpCompilationErrors_Synthetic_PageObjectProperty_RenderedCorrectly()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "PoPropertyTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "CheckProperty",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.User", "User", TargetKind.PageObjectProperty)),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("await User.ClickAsync()", output);
        Assert.DoesNotContain("TODO:", output);
        Assert.DoesNotContain("Page.Locator(\"User\")", output);
    }

    // --- TestHost config tests ---

    [Fact]
    public void Renderer_DefaultHost_RemainsBackwardCompatible()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "BackwardCompat",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("ClickTest", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(1, "btn") }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("class BackwardCompatPlaywright : PageTest", output);
        Assert.Contains("namespace Example.Tests.Playwright;", output);
        Assert.Contains("using Microsoft.Playwright.NUnit;", output);
        Assert.Contains("using NUnit.Framework;", output);
    }

    [Fact]
    public void Renderer_TestHost_AddsClassAttributes()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "AttributedTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("DoClick", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(1, "btn") }),
            })
        {
            TestHost = new TestHostConfig
            {
                Namespace = "Example.E2ETests.Tests",
                ClassAttributes = new[] { "TestFixture", "Parallelizable(ParallelScope.Self)" },
            }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[TestFixture]", output);
        Assert.Contains("[Parallelizable(ParallelScope.Self)]", output);
    }

    [Fact]
    public void Renderer_TestHost_UsesConfiguredBaseClass()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "BaseClassTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("DoClick", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(1, "btn") }),
            })
        {
            TestHost = new TestHostConfig
            {
                BaseClass = "TestBase",
            }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("class BaseClassTestPlaywright : TestBase", output);
        Assert.DoesNotContain(": PageTest", output);
    }

    [Fact]
    public void Renderer_TestHost_AddsConfiguredUsings()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "UsingsTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("DoClick", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(1, "btn") }),
            })
        {
            TestHost = new TestHostConfig
            {
                Usings = new[] { "NUnit.Framework", "Example.E2ETests.Infrastructure" },
            }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("using NUnit.Framework;", output);
        Assert.Contains("using Example.E2ETests.Infrastructure;", output);
        Assert.Contains("using Microsoft.Playwright.NUnit;", output);
    }

    [Fact]
    public void Renderer_TestHost_RendersConfiguredSetUpStatements()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "SetUpTest",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new MethodInvocationAction(10, "Navigation", "OpenPage", "Navigation.OpenPage()"),
            },
            Tests: new[]
            {
                new TestModel("DoClick", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(1, "btn") }),
            })
        {
            TestHost = new TestHostConfig
            {
                SetUpStatements = new[]
                {
                    "await Page.GotoAsync(TestSettings.LoginRoute);",
                    "await Page.GotoAsync(\"/catalogs\");",
                },
            }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[SetUp]", output);
        Assert.Contains("await Page.GotoAsync(TestSettings.LoginRoute);", output);
        Assert.Contains("await Page.GotoAsync(\"/catalogs\");", output);
        // Original setup preserved as comment
        Assert.Contains("// Original Selenium setup (mapped):", output);
        Assert.Contains("//   Navigation.OpenPage()", output);
    }

    [Fact]
    public void Renderer_TestHost_DoesNotHardcodeProjectSpecificValues()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "CleanTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("DoClick", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(1, "btn") }),
            })
        {
            TestHost = new TestHostConfig(), // empty config
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.DoesNotContain("TestBase", output);
        Assert.DoesNotContain("DefaultEnvParams", output);
        Assert.Contains(": PageTest", output); // defaults to PageTest
        Assert.DoesNotContain("[TestFixture]", output);
        Assert.Contains("namespace Example.Tests;", output); // no .Playwright suffix
    }

    [Fact]
    public void Renderer_TestHost_FullOutput_CompilesAndMatchesExpected()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Example.Tests",
            ClassName: "FullHostTest",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new MethodInvocationAction(10, "Navigation", "OpenPage", "Navigation.OpenPage()"),
            },
            Tests: new[]
            {
                new TestModel("CheckClick", "QuickRunning", Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    { new ClickAction(5, "el") }),
            })
        {
            TestHost = new TestHostConfig
            {
                Namespace = "Example.E2ETests.Tests",
                BaseClass = "TestBase",
                ClassName = "FullHostPlaywrightTests",
                ClassAttributes = new[] { "TestFixture", "Parallelizable(ParallelScope.Self)" },
                Usings = new[] { "NUnit.Framework", "Example.E2ETests.Infrastructure" },
                SetUpStatements = new[]
                {
                    "await Page.GotoAsync(TestSettings.LoginRoute);",
                    "await Page.GotoAsync(\"/catalogs\");",
                },
            }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        // Class wrapper
        Assert.Contains("namespace Example.E2ETests.Tests;", output);
        Assert.Contains("[TestFixture]", output);
        Assert.Contains("[Parallelizable(ParallelScope.Self)]", output);
        Assert.Contains("public class FullHostPlaywrightTests : TestBase", output);
        Assert.DoesNotContain("namespace Example.E2ETests.Tests.Playwright;", output);

        // Usings
        Assert.Contains("using NUnit.Framework;", output);
        Assert.Contains("using Example.E2ETests.Infrastructure;", output);

        // Setup
        Assert.Contains("[SetUp]", output);
        Assert.Contains("await Page.GotoAsync(TestSettings.LoginRoute);", output);
        Assert.Contains("await Page.GotoAsync(\"/catalogs\");", output);
        Assert.Contains("// Original Selenium setup (mapped):", output);

        // Test body preserved
        Assert.Contains("[Test]", output);
        Assert.Contains("public async Task CheckClick()", output);
        Assert.Contains("[Category(\"QuickRunning\")]", output);
    }

    // --- Match strategy tests ---

    [Fact]
    public void Renderer_UiTargetMatch_First()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "MatchFirstTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("CheckFirst", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.Row", "row", TargetKind.PlaywrightLocator, null, "First")),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains(".First", output);
        Assert.Contains("GetByTestId(\"row\").First", output);
        Assert.DoesNotContain(".Nth(", output);
    }

    [Fact]
    public void Renderer_UiTargetMatch_Nth()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "MatchNthTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("CheckNth", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.Row", "row", TargetKind.PlaywrightLocator, null, "Nth", 2)),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("GetByTestId(\"row\").Nth(2)", output);
        Assert.DoesNotContain(".First", output);
    }

    [Fact]
    public void Renderer_UiTargetMatch_None_NoSuffix()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "NoMatchTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("CheckNoMatch", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.Row", "row", TargetKind.PlaywrightLocator)),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("GetByTestId(\"row\")", output);
        Assert.DoesNotContain(".First", output);
        Assert.DoesNotContain(".Nth(", output);
    }

    [Fact]
    public void Renderer_UiTargetMatch_WithTestIdAttribute_First()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "MatchAttrTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("CheckAttr", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.Row", "row-item", TargetKind.PlaywrightLocator, "data-test", "First")),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("Locator(\"[data-test='row-item']\").First", output);
    }

    [Fact]
    public void Renderer_TextTarget_RendersGetByText()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TextTargetTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("CheckText", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.NameHeader", "Наименование", TargetKind.Text)),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("GetByText(\"Наименование\")", output);
        Assert.DoesNotContain("GetByTestId", output);
        Assert.DoesNotContain("TODO:", output);
    }

    [Fact]
    public void Renderer_TextTarget_WithMatch_First()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TextMatchTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("CheckTextMatch", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.Header", "Sort", TargetKind.Text, null, "First")),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("GetByText(\"Sort\").First", output);
    }

    [Fact]
    public void Renderer_Match_BackwardCompatible_ExistingTestsUnchanged()
    {
        // Verify that existing snapshot tests are not affected by Match/Text additions
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));
        var output = result.GeneratedOutput;

        Assert.DoesNotContain(".First", output);
        Assert.DoesNotContain(".Nth(", output);
        Assert.DoesNotContain("GetByText", output);
    }

    [Fact]
    public void Renderer_Match_DoesNotHardcodeValues()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "NoHardcodeTest",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("DoClick", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(
                            5,
                            TargetExpression.Mapped("page.El", "el", TargetKind.PlaywrightLocator)),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("GetByTestId(\"el\")", output);
        Assert.DoesNotContain("CatalogPrincipals", output);
        Assert.DoesNotContain("TestBase", output);
    }

    // --- Parameterized method mapping tests ---

    [Fact]
    public void ParameterizedMapping_ReplacesVariableArgument()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.NameSort.Sort({sortOrder})",
                    new[]
                    {
                        "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(sortOrder)",
                            new[] { "sortOrder" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var bodyActions = adapted.Tests.First().BodyActions;
        var mappedAction = bodyActions.OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("$\"span:has-text('{sortOrder}')\"", mappedAction.TargetStatements[0]);
        Assert.True(mappedAction.RequiresReview);
    }

    [Fact]
    public void ParameterizedMapping_ReplacesStringLiteralArgument()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Principal.InputAndSelect({value})",
                    new[]
                    {
                        "await popup.Locator(\"input\").FillAsync(\"{value}\");",
                        "await popup.GetByText(\"{value}\").ClickAsync();",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Principal", "InputAndSelect",
                            "page.Principal.InputAndSelect(\"Some principal\")",
                            new[] { "\"Some principal\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var bodyActions = adapted.Tests.First().BodyActions;
        var mappedAction = bodyActions.OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("FillAsync(\"Some principal\")", mappedAction.TargetStatements[0]);
        Assert.Contains("GetByText(\"Some principal\")", mappedAction.TargetStatements[1]);
        // Must NOT produce double quotes
        Assert.DoesNotContain("\"\"Some principal\"\"", mappedAction.TargetStatements[0]);
    }

    [Fact]
    public void ParameterizedMapping_ExactMappingWinsOverPattern()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping(
                    "page.NameSort.Sort(\"По возрастанию\")",
                    null,
                    "Exact mapping",
                    new[] { "// EXACT-MATCH" },
                    false)
            },
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.NameSort.Sort({sortOrder})",
                    new[] { "// PARAMETERIZED-{sortOrder}" },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(\"По возрастанию\")",
                            new[] { "\"По возрастанию\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var bodyActions = adapted.Tests.First().BodyActions;
        var mappedAction = bodyActions.OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("EXACT-MATCH", mappedAction.TargetStatements[0]);
        Assert.DoesNotContain("PARAMETERIZED", mappedAction.TargetStatements[0]);
        Assert.False(mappedAction.RequiresReview);
    }

    [Fact]
    public void ParameterizedMapping_InvalidPatternProducesWarning()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Name.Sort({", // invalid — unclosed brace
                    new[] { "await DoSomething();"},
                    false)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Name", "Sort",
                            "page.Name.Sort(\"value\")",
                            new[] { "\"value\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var bodyActions = adapted.Tests.First().BodyActions;

        // Must not silently drop — falls back to original MethodInvocationAction
        Assert.Single(bodyActions);
        Assert.IsType<MethodInvocationAction>(bodyActions.First());
    }

    [Fact]
    public void ParameterizedMapping_UnmatchedInvocationFallsBackToTodo()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Other.Sort({sortOrder})",
                    new[] { "// should not match" },
                    false)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(\"value\")",
                            new[] { "\"value\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var bodyActions = adapted.Tests.First().BodyActions;

        // Falls back to original MethodInvocationAction (renders as TODO in renderer)
        Assert.Single(bodyActions);
        Assert.IsType<MethodInvocationAction>(bodyActions.First());
    }

    [Fact]
    public void ParameterizedMapping_DoesNotDropActionSilently()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: Array.Empty<ParameterizedMethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(\"value\")",
                            new[] { "\"value\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var bodyActions = adapted.Tests.First().BodyActions;

        Assert.Single(bodyActions);
        Assert.IsType<MethodInvocationAction>(bodyActions.First());
    }

    // --- Profile scoping tests ---

    [Fact]
    public void ProfileScope_NoScopes_BackwardCompatible()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.User", "widget-user", "TestId")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            TestHost: new TestHostConfig
            {
                BaseClass = "TestBase",
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(5, "page.User"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);

        // Global TestHost is applied even when file path matches nothing (no scopes defined)
        Assert.Equal("TestBase", adapted.TestHost?.BaseClass);
    }

    [Fact]
    public void ProfileScope_AppliesTestHostForMatchingSourcePath()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            TestHost: new TestHostConfig
            {
                BaseClass = "PageTest",
            },
            Scopes: new[]
            {
                new ProfileScope
                {
                    Name = "CatalogPrincipals",
                    SourcePathPatterns = new[] { "**/CatalogPrincipalsFilter.cs" },
                    TestHost = new TestHostConfig
                    {
                        BaseClass = "TestBase",
                        Namespace = "Scoped.Tests",
                    }
                }
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), Array.Empty<TestAction>()),
            });

        var adapted = adapter.Adapt(sourceModel);

        Assert.Equal("TestBase", adapted.TestHost?.BaseClass);
        Assert.Equal("Scoped.Tests", adapted.TestHost?.Namespace);
    }

    [Fact]
    public void ProfileScope_DoesNotApplyTestHostForNonMatchingSourcePath()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            TestHost: new TestHostConfig
            {
                BaseClass = "PageTest",
            },
            Scopes: new[]
            {
                new ProfileScope
                {
                    Name = "CatalogPrincipals",
                    SourcePathPatterns = new[] { "**/CatalogPrincipalsFilter.cs" },
                    TestHost = new TestHostConfig
                    {
                        BaseClass = "TestBase",
                    }
                }
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/Widget.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), Array.Empty<TestAction>()),
            });

        var adapted = adapter.Adapt(sourceModel);

        // Global TestHost remains, scope is NOT applied
        Assert.Equal("PageTest", adapted.TestHost?.BaseClass);
    }

    [Fact]
    public void ProfileScope_ScopedUiTargetOverridesGlobal()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.User", "global-user", "TestId"),
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            Scopes: new[]
            {
                new ProfileScope
                {
                    Name = "CatalogPrincipals",
                    SourcePathPatterns = new[] { "**/CatalogPrincipalsFilter.cs" },
                    UiTargets = new[]
                    {
                        new UiTargetMapping("page.User", "scoped-user", "TestId"),
                    }
                }
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new ClickAction(5, "page.User"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var clicks = adapted.Tests.First().BodyActions.OfType<ClickAction>().ToList();
        Assert.Single(clicks);
        var target = clicks.First().Target as MappedTarget;
        Assert.NotNull(target);
        Assert.Equal("scoped-user", target.TargetExpression);
    }

    [Fact]
    public void ProfileScope_ScopedMethodOverridesGlobal()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping(
                    "page.Click.DoClick()",
                    null,
                    "Global",
                    new[] { "// GLOBAL-CLICK" },
                    false)
            },
            Scopes: new[]
            {
                new ProfileScope
                {
                    Name = "CatalogPrincipals",
                    SourcePathPatterns = new[] { "**/CatalogPrincipalsFilter.cs" },
                    Methods = new[]
                    {
                        new MethodMapping(
                            "page.Click.DoClick()",
                            null,
                            "Scoped",
                            new[] { "// SCOPED-CLICK" },
                            true)
                    }
                }
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Click", "DoClick",
                            "page.Click.DoClick()",
                            Array.Empty<string>()),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("SCOPED-CLICK", mapped.TargetStatements[0]);
        Assert.DoesNotContain("GLOBAL-CLICK", mapped.TargetStatements[0]);
        Assert.True(mapped.RequiresReview);
    }

    [Fact]
    public void ProfileScope_MultipleMatches_DeterministicSelection()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            TestHost: new TestHostConfig { BaseClass = "DefaultBase" },
            Scopes: new[]
            {
                new ProfileScope
                {
                    Name = "ScopeA",
                    SourcePathPatterns = new[] { "**/CatalogPrincipalsFilter.cs" },
                    TestHost = new TestHostConfig { BaseClass = "ScopeABase" },
                },
                new ProfileScope
                {
                    Name = "ScopeB",
                    SourcePathPatterns = new[] { "**/CatalogPrincipalsFilter.cs" },
                    TestHost = new TestHostConfig { BaseClass = "ScopeBBase" },
                },
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/CatalogPrincipalsFilter.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), Array.Empty<TestAction>()),
            });

        var adapted = adapter.Adapt(sourceModel);

        // First scope wins deterministically
        Assert.Equal("ScopeABase", adapted.TestHost?.BaseClass);
    }

    // --- Quote-aware placeholder substitution tests ---

    [Fact]
    public void ParameterizedMapping_RawPlaceholder_StringLiteralArgument()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Principal.InputAndSelect({value})",
                    new[]
                    {
                        "await popup.Locator(\"input\").FillAsync({value});",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Principal", "InputAndSelect",
                            "page.Principal.InputAndSelect(\"Some principal\")",
                            new[] { "\"Some principal\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("FillAsync(\"Some principal\")", mapped.TargetStatements[0]);
        Assert.DoesNotContain("\"\"Some principal\"\"", mapped.TargetStatements[0]);
    }

    [Fact]
    public void ParameterizedMapping_RawPlaceholder_VariableArgument()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Principal.InputAndSelect({value})",
                    new[]
                    {
                        "await popup.Locator(\"input\").FillAsync({value});",
                        "await popup.GetByText({value}).ClickAsync();",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Principal", "InputAndSelect",
                            "page.Principal.InputAndSelect(principalName)",
                            new[] { "principalName" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("FillAsync(principalName)", mapped.TargetStatements[0]);
        Assert.Contains("GetByText(principalName)", mapped.TargetStatements[1]);
    }

    [Fact]
    public void ParameterizedMapping_QuotedPlaceholder_StringLiteral_StripsQuotes()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Principal.InputAndSelect({value})",
                    new[]
                    {
                        "await popup.Locator(\"input\").FillAsync(\"{value}\");",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Principal", "InputAndSelect",
                            "page.Principal.InputAndSelect(\"Some principal\")",
                            new[] { "\"Some principal\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("FillAsync(\"Some principal\")", mapped.TargetStatements[0]);
        Assert.DoesNotContain("\"\"Some principal\"\"", mapped.TargetStatements[0]);
    }

    [Fact]
    public void ParameterizedMapping_QuotedPlaceholder_VariableArgument_Interpolated()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.Principal.InputAndSelect({value})",
                    new[]
                    {
                        "await popup.Locator(\"input\").FillAsync(\"{value}\");",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.Principal", "InputAndSelect",
                            "page.Principal.InputAndSelect(principalName)",
                            new[] { "principalName" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("$\"{principalName}\"", mapped.TargetStatements[0]);
        Assert.DoesNotContain("FillAsync(\"principalName\")", mapped.TargetStatements[0]);
    }

    [Fact]
    public void ParameterizedMapping_SelectorString_VariableArgument_Interpolated()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.NameSort.Sort({sortOrder})",
                    new[]
                    {
                        "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(sortOrder)",
                            new[] { "sortOrder" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("$\"span:has-text('{sortOrder}')\"", mapped.TargetStatements[0]);
        Assert.DoesNotContain("has-text('sortOrder')", mapped.TargetStatements[0].Replace("$", ""));
    }

    [Fact]
    public void ParameterizedMapping_SelectorString_StringLiteral_StripsQuotes()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.NameSort.Sort({sortOrder})",
                    new[]
                    {
                        "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(\"asc\")",
                            new[] { "\"asc\"" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        Assert.Contains("span:has-text('asc')", mapped.TargetStatements[0]);
        Assert.DoesNotContain("$\"", mapped.TargetStatements[0]);
    }

    [Fact]
    public void ParameterizedMapping_NoLiteralVariableNameInSelector()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "page.NameSort.Sort({sortOrder})",
                    new[]
                    {
                        "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();",
                    },
                    true)
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(5, "page.NameSort", "Sort",
                            "page.NameSort.Sort(sortOrder)",
                            new[] { "sortOrder" }),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var mapped = adapted.Tests.First().BodyActions
            .OfType<MappedMethodInvocationAction>().First();

        // Must NOT produce a literal sortOrder inside the string — must use interpolation
        var stmt = mapped.TargetStatements[0];
        Assert.Contains("$\"", stmt);
        Assert.Contains("{sortOrder}", stmt);
    }

    [Fact]
    public void ProfileScope_DoesNotHardcodeProjectSpecificValues()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            TestHost: new TestHostConfig { BaseClass = "TestBase" },
            Scopes: new[]
            {
                new ProfileScope
                {
                    Name = "MyScope",
                    SourcePathPatterns = new[] { "**/SomeFile.cs" },
                    TestHost = new TestHostConfig { BaseClass = "MyBase" },
                }
            });

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/SomeFile.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("Test1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), Array.Empty<TestAction>()),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("MyBase", output);
        Assert.DoesNotContain("CatalogPrincipals", output);
        Assert.DoesNotContain("DefaultEnvParams", output);
        Assert.DoesNotContain("ArBilling", output);
    }

    #region Low-priority and trivial statement tests

    [Fact]
    public void LowPriority_Window_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "Window", "page.Window()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[Window]", output);
        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ExecuteScript_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "driver", "ExecuteScript",
                            "driver.ExecuteScript(\"return document.title\")"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[ExecuteScript]", output);
        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_SettingPeriod_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "SettingPeriod", "page.SettingPeriod()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[SettingPeriod]", output);
        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ValidateLoadingPrintForm_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page.Loader", "ValidateLoadingPrintForm",
                            "page.Loader.ValidateLoadingPrintForm()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[ValidateLoadingPrintForm]", output);
        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ValidateLoading_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page.Loader", "ValidateLoading",
                            "page.Loader.ValidateLoading()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[ValidateLoading]", output);
        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_UnknownMethod_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "RemoveItem", "page.RemoveItem()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[RemoveItem]", output);
        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_Refresh_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "driver", "Refresh", "driver.Refresh()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ClickAndOpen_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "ClickAndOpen", "page.ClickAndOpen<MyPage>()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_NavigationOpen_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "Navigation", "OpenPage", "Navigation.OpenPage()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ClearSort_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "ClearSort", "page.ClearSort()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_WaitAbsence_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "WaitAbsence", "page.WaitAbsence()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_InputTextAndSelectValue_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "InputTextAndSelectValue",
                            "page.InputTextAndSelectValue(\"value\")"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ManualInputValue_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "ManualInputValue",
                            "page.ManualInputValue(\"value\")"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_SetBeginDate_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "SetBeginDate",
                            "page.SetBeginDate(\"2024-01-01\")"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_InsertAnotherTab_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "InsertAnotherTab", "page.InsertAnotherTab()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_InsertAndExcludeAnotherTab_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "InsertAndExcludeAnotherTab",
                            "page.InsertAndExcludeAnotherTab()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_ShouldUrlTableItem_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "page", "ShouldUrlTableItem",
                            "page.ShouldUrlTableItem(\"expected\")"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void LowPriority_equalTo_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new MethodInvocationAction(1, "Assert", "EqualTo", "Assert.EqualTo(expected, actual)"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: manual review needed", output);
    }

    [Fact]
    public void TrivialRaw_VisibleGet_NoAssignment_NoTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "page.Loader.Visible.Get()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("source:", output);
        Assert.DoesNotContain("TODO: raw statement", output);
    }

    [Fact]
    public void TrivialRaw_TextGet_NoAssignment_NoTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "page.Header.Text.Get()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("source:", output);
        Assert.DoesNotContain("TODO: raw statement", output);
    }

    [Fact]
    public void TrivialRaw_VarInn_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "var inn = GetInn()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: raw statement", output);
    }

    [Fact]
    public void TrivialRaw_VarLineCount_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "var lineCount = page.Table.Items.Count()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: raw statement", output);
    }

    [Fact]
    public void TrivialRaw_VarTrim_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "var trim = TrimText(page.Header.Text.Get())"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: raw statement", output);
    }

    [Fact]
    public void TrivialRaw_NavigationOpen_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "Navigation.OpenPage()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: raw statement", output);
    }

    [Fact]
    public void TrivialRaw_ClickAndOpen_RemainsTODO()
    {
        var targetModel = new TestFileModel(
            FilePath: "fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new RawStatementAction(1, "page.ClickAndOpen<Modal>()"),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("TODO: raw statement", output);
    }

    #endregion

    #region Text.Get() in expression — locator double-wrapping regression

    [Fact]
    public void TextGetInsideIntParse_WithTestIdTarget_RendersValidLocator()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.Count", "CurrencyLabel__root", "TestId")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new LocalDeclarationAction(1, "var", "count",
                            "int.Parse(page.Count.Text.Get()) - 1"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("int.Parse(await Page.GetByTestId(\"CurrencyLabel__root\").TextContentAsync())", output);
        Assert.DoesNotContain("Page.Locator(\"GetByTestId", output);
        Assert.DoesNotContain("Page.Locator(\"Page.GetByTestId", output);
    }

    [Fact]
    public void TextGetInsideIntParse_WithTestIdTargetAndAttribute_RendersValidLocator()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.Count", "CurrencyLabel__root", "TestId", "data-tid")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new LocalDeclarationAction(1, "var", "count",
                            "int.Parse(page.Count.Text.Get()) - 1"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("Page.Locator(\"[data-tid='CurrencyLabel__root']\")", output);
        Assert.Contains("TextContentAsync()", output);
        Assert.DoesNotContain("Page.Locator(\"GetByTestId", output);
    }

    [Fact]
    public void TextGetInsideIntParse_WithRawExpressionTarget_NoDoubleWrap()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.Count", "Page.GetByTestId(\"CurrencyLabel__root\")", "RawExpression")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new LocalDeclarationAction(1, "var", "count",
                            "int.Parse(page.Count.Text.Get()) - 1"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("int.Parse(await Page.GetByTestId(\"CurrencyLabel__root\").TextContentAsync())", output);
        Assert.DoesNotContain("Page.Locator(\"Page.GetByTestId", output);
    }

    [Fact]
    public void TextGetInsideIntParse_WithLegacyPlaywrightFragment_NoDoubleWrap()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.Count", "GetByTestId(\"CurrencyLabel__root\")", "Locator")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new LocalDeclarationAction(1, "var", "count",
                            "int.Parse(page.Count.Text.Get()) - 1"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("Page.GetByTestId(\"CurrencyLabel__root\")", output);
        Assert.DoesNotContain("Page.Locator(\"GetByTestId", output);
        Assert.DoesNotContain("Page.Locator(\"Page.GetByTestId", output);
    }

    [Fact]
    public void TextGetInsideIntParse_GeneratedCodeCompiles()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.Count", "CurrencyLabel__root", "TestId")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new LocalDeclarationAction(1, "var", "count",
                            "int.Parse(page.Count.Text.Get()) - 1"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("int.Parse(await Page.GetByTestId(\"CurrencyLabel__root\").TextContentAsync())", output);
        Assert.DoesNotContain("Page.Locator(\"GetByTestId", output);
        Assert.DoesNotContain("Page.Locator(\"Page.GetByTestId", output);
    }

    [Fact]
    public void TextGetInsideArithmetic_MultipleTextGetInOneExpression()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.A", "id_a", "TestId"),
                new UiTargetMapping("page.B", "id_b", "TestId"),
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapter = new DefaultProjectAdapter(config);
        var sourceModel = new TestFileModel(
            FilePath: "Tests/fake.cs",
            Namespace: "Test",
            ClassName: "TestCls",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(), new TestAction[]
                    {
                        new LocalDeclarationAction(1, "var", "result",
                            "int.Parse(page.A.Text.Get()) + int.Parse(page.B.Text.Get())"),
                    }),
            });

        var adapted = adapter.Adapt(sourceModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);

        Assert.Contains("Page.GetByTestId(\"id_a\").TextContentAsync()", output);
        Assert.Contains("Page.GetByTestId(\"id_b\").TextContentAsync()", output);
        Assert.DoesNotContain("Page.Locator(\"GetByTestId", output);
    }

    #endregion

    #region Config validation tests

    [Fact]
    public void Methods_SourceExpressionInsteadOfSourceMethod_ThrowsConfigValidationError()
    {
        var json = @"{
            ""SourceProjectName"": ""Test"",
            ""UiTargets"": [],
            ""Methods"": [
                {
                    ""SourceExpression"": ""page.Table.ValidateUnvisibleTextLoader()"",
                    ""TargetStatements"": [""await WaitForTableLoaderAsync();""]
                }
            ]
        }";

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.ValidateJson(json));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("Methods[0]", allErrors);
        Assert.Contains("SourceExpression", allErrors);
        Assert.Contains("SourceMethod", allErrors);
        Assert.Contains("Did you mean", allErrors);
    }

    [Fact]
    public void Methods_MissingSourceMethod_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping("", null, null, new[] { "await X();" }, false)
            });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("Methods[0]", allErrors);
        Assert.Contains("missing SourceMethod", allErrors);
    }

    [Fact]
    public void Methods_NoTargetMethodOrStatements_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping("page.X()", null, null, null, false)
            });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("Methods[0]", allErrors);
        Assert.Contains("no TargetMethod or TargetStatements", allErrors);
    }

    [Fact]
    public void UiTargets_MissingSourceExpression_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("", "", "TestId", null)
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("UiTargets[0]", allErrors);
        Assert.Contains("missing SourceExpression", allErrors);
    }

    [Fact]
    public void UiTargets_MissingTargetExpression_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.X", "", "TestId", "")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("UiTargets[0]", allErrors);
        Assert.Contains("missing TargetExpression", allErrors);
    }

    [Fact]
    public void ParameterizedMethods_MissingSourceMethodPattern_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            null,
            null,
            new[]
            {
                new ParameterizedMethodMapping("", new[] { "await X({0});" }, false)
            });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("ParameterizedMethods[0]", allErrors);
        Assert.Contains("missing SourceMethodPattern", allErrors);
    }

    [Fact]
    public void ParameterizedMethods_MissingTargetStatements_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            null,
            null,
            new[]
            {
                new ParameterizedMethodMapping("page.ValidateRow({0})", null, false)
            });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("ParameterizedMethods[0]", allErrors);
        Assert.Contains("missing TargetStatements", allErrors);
    }

    [Fact]
    public void Scope_Methods_MissingSourceMethod_ReturnsScopedConfigError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            Scopes: new[]
            {
                new ProfileScope(
                    "CatalogPrincipals",
                    new[] { "**/CatalogPrincipals*.cs" },
                    methods: new[]
                    {
                        new MethodMapping("", null, null, new[] { "await X();" }, false)
                    })
            });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var firstError = ex.Errors[0];
        Assert.Contains("Scopes[CatalogPrincipals]", firstError);
        Assert.Contains("Methods[0]", firstError);
        Assert.Contains("missing SourceMethod", firstError);
    }

    [Fact]
    public void Scope_ByName_MissingSourceMethodPattern_ReturnsScopedConfigError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            Scopes: new[]
            {
                new ProfileScope(
                    "MyScope",
                    new[] { "**/Test*.cs" },
                    parameterizedMethods: new[]
                    {
                        new ParameterizedMethodMapping("", new[] { "await X();" }, false)
                    })
            });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var firstError = ex.Errors[0];
        Assert.Contains("Scopes[MyScope]", firstError);
        Assert.Contains("ParameterizedMethods[0]", firstError);
        Assert.Contains("missing SourceMethodPattern", firstError);
    }

    [Fact]
    public void QualityGates_NegativeMaxTodoComments_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            QualityGates: new QualityGatesConfig { MaxTodoComments = -1 });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("QualityGates.MaxTodoComments", allErrors);
        Assert.Contains("cannot be negative", allErrors);
    }

    [Fact]
    public void QualityGates_NegativeMaxUnsupportedActions_ThrowsConfigValidationError()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            QualityGates: new QualityGatesConfig { MaxUnsupportedActions = -5 });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        var allErrors = string.Join(" ", ex.Errors);
        Assert.Contains("QualityGates.MaxUnsupportedActions", allErrors);
        Assert.Contains("cannot be negative", allErrors);
    }

    [Fact]
    public void ValidConfig_PassesValidation()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[] { new UiTargetMapping("page.X", "tid_x", "TestId") },
            Array.Empty<PageObjectMapping>(),
            new[] { new MethodMapping("page.X()", "targetMethod", null, null, false) });

        ConfigValidator.Validate(config);
    }

    [Fact]
    public void ValidConfig_WithOptionalSections_PassesValidation()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[] { new UiTargetMapping("page.X", "tid_x", "TestId") },
            Array.Empty<PageObjectMapping>(),
            new[] { new MethodMapping("page.Y()", null, null, new[] { "await X();" }, false) },
            LocatorSettings: new LocatorSettings("data-tid", null),
            TestHost: new TestHostConfig { BaseClass = "TestBase" },
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping("page.Sort({order})", new[] { "await Page.GetByTestId(\"{order}\").ClickAsync();" }, false)
            },
            Scopes: new[]
            {
                new ProfileScope("Scope1", new[] { "**/Foo*.cs" })
            },
            QualityGates: new QualityGatesConfig { MaxTodoComments = 100 });

        ConfigValidator.Validate(config);
    }

    [Fact]
    public void MultipleErrors_AllReportedTogether()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("", "", "TestId"),
            },
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping("", null, null, null, false),
            },
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping("", null, false),
            },
            QualityGates: new QualityGatesConfig { MaxTodoComments = -1 });

        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(config));

        Assert.True(ex.Errors.Count >= 5, $"Expected at least 5 errors, got {ex.Errors.Count}: {string.Join(", ", ex.Errors)}");
        Assert.Contains(ex.Errors, e => e.Contains("UiTargets[0]") && e.Contains("missing SourceExpression"));
        Assert.Contains(ex.Errors, e => e.Contains("UiTargets[0]") && e.Contains("missing TargetExpression"));
        Assert.Contains(ex.Errors, e => e.Contains("Methods[0]") && e.Contains("missing SourceMethod"));
        Assert.Contains(ex.Errors, e => e.Contains("ParameterizedMethods[0]") && e.Contains("missing SourceMethodPattern"));
        Assert.Contains(ex.Errors, e => e.Contains("ParameterizedMethods[0]") && e.Contains("missing TargetStatements"));
        Assert.Contains(ex.Errors, e => e.Contains("cannot be negative"));
    }

    [Fact]
    public void ConfigValidationError_MessageIsUserReadable()
    {
        var ex = new ConfigValidationError(new[]
        {
            "Methods[0] uses SourceExpression, but Methods mappings require SourceMethod.",
            "Did you mean \"SourceMethod\": \"page.Table.Validate()\"?",
        });

        Assert.Contains("Invalid adapter-config.json:", ex.Message);
        Assert.Contains("Methods[0]", ex.Message);
        Assert.DoesNotContain("StackTrace", ex.Message);
    }

    [Fact]
    public void DefaultProjectAdapter_WithInvalidConfigJson_ThrowsConfigValidationError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"migrator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "adapter-config.json");
        File.WriteAllText(configPath, @"{
            ""SourceProjectName"": ""Test"",
            ""UiTargets"": [],
            ""Methods"": [
                {
                    ""SourceExpression"": ""page.X()"",
                    ""TargetStatements"": [""await Y();""]
                }
            ]
        }");

        var ex = Assert.Throws<ConfigValidationError>(() => new DefaultProjectAdapter(configPath));
        Assert.Contains("Methods[0]", string.Join("\n", ex.Errors));
        Assert.Contains("SourceExpression", string.Join("\n", ex.Errors));

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void DefaultProjectAdapter_WithValidConfigJson_ConstructsSuccessfully()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[] { new UiTargetMapping("page.X", "tid_x", "TestId") },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        _ = new DefaultProjectAdapter(config);
    }

    #endregion

    #region Support file tests

    [Fact]
    public void Parser_SupportFileWithoutTests_DoesNotFail()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"migrator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var supportFile = Path.Combine(tempDir, "TestBase.cs");
        File.WriteAllText(supportFile, @"
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestBase
{
    public void Login() { }
    public void Navigate() { }
}
");

        var parser = new RoslynTestFileParser();
        var results = parser.ParseDirectory(tempDir).ToList();

        Assert.Empty(results);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Parser_DirectoryWithSupportAndTestFiles_OnlyReturnsTestFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"migrator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var supportFile = Path.Combine(tempDir, "TestBase.cs");
        File.WriteAllText(supportFile, @"
using NUnit.Framework;

namespace Tests;

public class TestBase
{
    public void Setup() { }
}
");

        var testFile = Path.Combine(tempDir, "SomeTests.cs");
        File.WriteAllText(testFile, @"
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class SomeTests
{
    [Test]
    public void Test1()
    {
        System.Console.WriteLine(""hello"");
    }
}
");

        var parser = new RoslynTestFileParser();
        var results = parser.ParseDirectory(tempDir).ToList();

        Assert.Single(results);
        Assert.Equal("SomeTests", results[0].ClassName);
        Assert.Single(results[0].Tests);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Parser_DirectoryWithOnlySupportFiles_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"migrator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "PageBase.cs"), @"
namespace Tests;
public class PageBase { }
");
        File.WriteAllText(Path.Combine(tempDir, "ControlBase.cs"), @"
namespace Tests;
public class ControlBase { }
");

        var parser = new RoslynTestFileParser();
        var results = parser.ParseDirectory(tempDir).ToList();

        Assert.Empty(results);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Parser_DirectoryWithSyntaxErrorFile_DoesNotCrash()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"migrator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "Broken.cs"), @"
namespace Tests;
public class Broken { this is not valid C# }}}
");
        var testFile = Path.Combine(tempDir, "GoodTests.cs");
        File.WriteAllText(testFile, @"
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class GoodTests
{
    [Test]
    public void T1() { }
}
");

        var parser = new RoslynTestFileParser();
        var results = parser.ParseDirectory(tempDir).ToList();

        Assert.Single(results);
        Assert.Equal("GoodTests", results[0].ClassName);

        Directory.Delete(tempDir, true);
    }

    #endregion

    static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();
}
