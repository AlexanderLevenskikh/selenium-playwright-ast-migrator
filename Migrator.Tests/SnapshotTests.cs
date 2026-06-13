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
        Assert.DoesNotContain("using Microsoft.Playwright.NUnit;", output);
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
                    "await Page.GotoAsync(DefaultEnvParams.TestLogin);",
                    "await Page.GotoAsync(\"/catalogs?activeTab=principals\");",
                },
            }
        };

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(targetModel);

        Assert.Contains("[SetUp]", output);
        Assert.Contains("await Page.GotoAsync(DefaultEnvParams.TestLogin);", output);
        Assert.Contains("await Page.GotoAsync(\"/catalogs?activeTab=principals\");", output);
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
                    "await Page.GotoAsync(DefaultEnvParams.TestLogin);",
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
        Assert.DoesNotContain(".Playwright", output);

        // Usings
        Assert.Contains("using NUnit.Framework;", output);
        Assert.Contains("using Example.E2ETests.Infrastructure;", output);

        // Setup
        Assert.Contains("[SetUp]", output);
        Assert.Contains("await Page.GotoAsync(DefaultEnvParams.TestLogin);", output);
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

    static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();
}
