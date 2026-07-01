using System.IO;
using System.Linq;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.Roslyn.Recognizers;
using Migrator.SeleniumCSharp;

namespace Migrator.Tests;

public class TicketRegressionTests
{
    [Fact]
    public void SuppressedMethodPattern_RunsBeforeSourceOnlySafety_ForLocalDeclaration()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new LocalDeclarationAction(
                10,
                "discountOnProductPage",
                "var",
                "page.GoToDiscountsPage(Product.Multiproduct)")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "page" },
            SuppressedMethodPatterns: new[] { "*page.GoToDiscountsPage(*)" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("source statement suppressed by adapter-config", output);
        Assert.Contains("page.GoToDiscountsPage(Product.Multiproduct)", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
        Assert.DoesNotMatch(@"(?m)^\s*var\s+discountOnProductPage\s*=", output);
    }

    [Fact]
    public void ParameterizedMethodPattern_MatchesMultilineFluentChain()
    {
        var source = "tariffModel.AvailableClientTypes.Should()\n    .BeEquivalentTo([ClientType.IndividualBusinessman])";
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Should().BeEquivalentTo({arg})",
                    new[] { "// mapped {source} {arg}" },
                    requiresReview: false)
            });

        var model = CreateModel(new TestAction[]
        {
            new MethodInvocationAction(
                20,
                "tariffModel.AvailableClientTypes.Should()",
                "BeEquivalentTo",
                source,
                new[] { "[ClientType.IndividualBusinessman]" })
        });

        var adapted = new DefaultProjectAdapter(config).Adapt(model);
        var mapped = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());

        Assert.Contains("mapped", mapped.TargetStatements.Single());
        Assert.DoesNotContain("Should()", mapped.TargetStatements.Single());
    }

    [Fact]
    public void MappedMethodInvocation_NormalizesConfigStringSyntaxAndSplitOperators()
    {
        var model = CreateModel(new TestAction[]
        {
            new MappedMethodInvocationAction(
                30,
                "rulePage.Input.Press(Enter)",
                new[]
                {
                    "await Page.GetByTestId('forbidden-informer').PressAsync('Enter');",
                    "await Assertions.Expect(rows).Not.ToContainAsync(x => x.Count = =0);"
                })
        });

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("Page.GetByTestId(\"forbidden-informer\").PressAsync(\"Enter\")", output);
        Assert.Contains("x => x.Count ==0", output);
        Assert.DoesNotContain("'forbidden-informer'", output);
        Assert.DoesNotContain("= =", output);
    }

    [Fact]
    public void NUnitAssertThat_TextDoesContain_ResolvesThroughUiTarget()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new AssertThatAction(
                34,
                "page.Status.Text",
                "Does.Contain(\"Ready\")")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.Status",
                    "GetByTestId(\"status\")",
                    "TestId")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var adapted = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(adapted);

        Assert.Contains("await Expect(Page.GetByTestId(\"status\")).ToContainTextAsync(\"Ready\")", output);
        Assert.DoesNotContain("ASSERTION_CONSTRAINT", output);
    }


    [Fact]
    public void SyntaxFallbackRawInvocation_UsesParameterizedMappingAndUiTarget()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new RawStatementAction(
                40,
                "page.PeriodBeginDateSort.Sort(sortOrder)")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.PeriodBeginDateSort",
                    "Page.Locator(\"[data-tid=SortBox__root]\").Filter(new LocatorFilterOptions { HasText = \"Начало периода\" })",
                    "RawExpression")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Sort({sortOrder})",
                    new[] { "await {TARGET}.Locator(\"[data-tid=SortBox__arrow]\").ClickAsync();" },
                    requiresReview: false)
            },
            SourceOnlyIdentifiers: new[] { "page" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("SortBox__arrow", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
        Assert.DoesNotContain("TODO: uses source-only identifier 'page'", output);
    }

    [Fact]
    public void SyntaxFallbackRawClick_UsesUiTarget_WhenSemanticRecognizerMissedIt()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new RawStatementAction(
                48,
                "page.Table.Items.ElementAt(9).Click()")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.Table",
                    "Page.GetByTestAttribute(\"table\")",
                    "RawExpression")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "page" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("ClickAsync", output);
        Assert.Contains("Page.GetByTestAttribute(\"table\")", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
    }

    [Fact]
    public void Renderer_TestHost_TargetTestFrameworkXunit_RendersXunitAttributesAndUsings()
    {
        var model = CreateModel(Array.Empty<TestAction>()) with
        {
            TestHost = new TestHostConfig
            {
                TargetTestFramework = "xunit",
                Namespace = "Sample.Pw.Tests",
                BaseClass = "TestBase",
                ClassAttributes = new[] { "Collection(\"Sequential\")" },
                Usings = new[] { "Microsoft.Playwright.Extensions.Xunit", "Xunit" }
            }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("using Microsoft.Playwright.Extensions.Xunit;", output);
        Assert.Contains("using Xunit;", output);
        Assert.Contains("[Collection(\"Sequential\")]", output);
        Assert.Contains("[Fact(DisplayName = \"GeneratedTest\")]", output);
        Assert.DoesNotContain("NUnit.Framework", output);
        Assert.DoesNotContain("Microsoft.Playwright.NUnit", output);
        Assert.DoesNotContain("[Test]", output);
    }

    [Fact]
    public void Renderer_TestIdKind_DoesNotDoubleWrapGetByTestIdExpression()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new ClickAction(50, "discountLockingBlock.Unlock")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "discountLockingBlock.Unlock",
                    "GetByTestId(\"unlock-product-discounts\")",
                    "TestId",
                    testIdAttribute: "data-tid")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("Page.Locator(\"[data-tid='unlock-product-discounts']\")", output);
        Assert.DoesNotContain("GetByTestId(\\\"unlock-product-discounts\\\")", output);
        Assert.DoesNotContain("[data-tid='GetByTestId", output);
    }



    [Fact]
    public void WaitInvocationRecognizer_UsesConfiguredWaitPolicy()
    {
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            WaitPolicies: new[]
            {
                new WaitPolicyMapping
                {
                    SourceMethod = "WaitDisabled",
                    Kind = "ActionabilityElided"
                }
            });
        var recognizer = new WaitInvocationRecognizer(RecognizerOptions.FromConfig(config));

        var action = recognizer.TryRecognize(new InvocationContext(
            "WaitDisabled",
            "discountSettingsPage.Save",
            "discountSettingsPage.Save.WaitDisabled()",
            62,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>()));

        var wait = Assert.IsType<WaitForAction>(action);
        Assert.Equal(WaitForKind.ActionabilityElided, wait.Kind);
        Assert.Equal("WaitDisabled", wait.SourceMethod);
    }

    [Fact]
    public void GenericReceiverMethodMapping_ReplacesElementWithResolvedTarget()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new MethodInvocationAction(
                60,
                "discountSettingsPage.Save",
                "WaitDisabled",
                "discountSettingsPage.Save.WaitDisabled()",
                Array.Empty<string>(),
                RecognitionConfidence.SyntaxFallback)
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "discountSettingsPage.Save",
                    @"Page.GetByTestId(""save"")",
                    "RawExpression")
            },
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping(
                    "element.WaitDisabled()",
                    null,
                    null,
                    new[] { "await Expect(element).ToBeDisabledAsync();" },
                    false)
            });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains(@"Page.GetByTestId(""save"")", output);
        Assert.Contains("ToBeDisabledAsync", output);
        Assert.DoesNotContain("Expect(element)", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
    }

    [Fact]
    public void ParameterizedGenericClick_SubstitutesSourcePlaceholder()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new MethodInvocationAction(
                61,
                "tariffCard.TariffListPageLink",
                "Click",
                "tariffCard.TariffListPageLink.Click<TariffsOnProductPage>()",
                Array.Empty<string>(),
                "tariffListPage",
                RecognitionConfidence.SyntaxFallback)
        });
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Click<{T}>()",
                    new[] { "var {result} = await {source}.ClickAsync(); // target {T}" },
                    requiresReview: true)
            });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var mapped = Assert.IsType<MappedMethodInvocationAction>(model.Tests.Single().BodyActions.Single());

        Assert.Contains("tariffCard.TariffListPageLink.ClickAsync", mapped.TargetStatements.Single());
        Assert.Contains("TariffsOnProductPage", mapped.TargetStatements.Single());
        Assert.DoesNotContain("{source}", mapped.TargetStatements.Single());
    }

    [Fact]
    public void SuppressedBooleanDeclaration_EmitsCompileOnlyStubForLaterCondition()
    {
        var model = CreateModel(new TestAction[]
        {
            new LocalDeclarationAction(
                70,
                "element1",
                "var",
                "page.HasWarningAccept.Visible.Get()"),
            new ConditionalBlockAction(
                71,
                "element1",
                new TestAction[] { new RawStatementAction(72, "Console.WriteLine(\"ok\")") },
                Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
                Array.Empty<TestAction>())
        }) with
        {
            SuppressedMethodPatterns = new[] { "*page.*.Visible.Get(*)" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("bool element1 = default", output);
        Assert.Contains("SUPPRESSED_DECLARATION_STUB", output);
        Assert.Contains("if (element1)", output);
        Assert.DoesNotContain("CS0103", output);
        Assert.DoesNotContain("CONDITIONAL_UNRESOLVED_SYMBOL", output);
    }

    [Fact]
    public void ConditionalBlock_WithUnknownCondition_IsCommentedInsteadOfEmittingUndefinedSymbol()
    {
        var model = CreateModel(new TestAction[]
        {
            new ConditionalBlockAction(
                80,
                "element2",
                new TestAction[] { new RawStatementAction(81, "Console.WriteLine(\"bad\")") },
                Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
                Array.Empty<TestAction>())
        });

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("CONDITIONAL_UNRESOLVED_SYMBOL", output);
        Assert.Contains("if (element2) { ... }", output);
        Assert.DoesNotMatch(@"(?m)^\s*if\s*\(element2\)", output);
    }


    [Fact]
    public void WebDriverFindElements_StaticXPath_ParsesAsLocatorDeclaration()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;
using OpenQA.Selenium;

public class SampleTests
{
    [Test]
    public void GeneratedTest()
    {
        var valueSum = WebDriver.FindElements(By.XPath(""//div[@data-tid='SidePageBody__root']//span[@data-tid='CurrencyLabel__root']""));
    }
}
");

            var model = new RoslynTestFileParser().Parse(file);
            var action = Assert.IsType<LocatorDeclarationAction>(model.Tests.Single().BodyActions.Single());

            Assert.Equal("valueSum", action.VariableName);
            Assert.Contains("Page.Locator", action.LocatorExpression);
            Assert.Contains("xpath=//div[@data-tid='SidePageBody__root']//span[@data-tid='CurrencyLabel__root']", action.LocatorExpression);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void WebDriverFindElements_LocalElementAt_CanResolveDownstreamTextAssertion()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new LocatorDeclarationAction(
                100,
                "valueSum",
                "Page.Locator(\"xpath=//div[@data-tid='SidePageBody__root']//span[@data-tid='CurrencyLabel__root']\")",
                "WebDriver.FindElements(By.XPath(\"//div[@data-tid='SidePageBody__root']//span[@data-tid='CurrencyLabel__root']\"))"),
            new TextAssertionAction(
                101,
                "valueSum.ElementAt(1)",
                TextAssertionKind.TextEquals,
                "\"7 854 000,00 ₽\"",
                RecognitionConfidence.SyntaxFallback)
        });

        var model = new DefaultProjectAdapter(new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>())).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("var valueSum = Page.Locator", output);
        Assert.Contains("valueSum.Nth(1)", output);
        Assert.Contains("ToHaveTextAsync(\"7 854 000,00 ₽\")", output);
        Assert.DoesNotContain("UNRESOLVED_SYMBOL", output);
        Assert.DoesNotContain("MISSING_MAPPING", output);
    }

    [Fact]
    public void UrlAssertion_AllowsTargetKnownTypeExpression()
    {
        var model = CreateModel(new TestAction[]
        {
            new UrlAssertionAction(84, UrlAssertionKind.UrlEquals, "Urls.BaseUrlCatalogPartners")
        }) with
        {
            TargetKnownTypes = new[] { "Urls" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("await Expect(Page).ToHaveURLAsync(Urls.BaseUrlCatalogPartners)", output);
        Assert.DoesNotContain("EXTERNAL_URL_VARIABLE", output);
        Assert.DoesNotContain("// await Expect(Page).ToHaveURLAsync(Urls.BaseUrlCatalogPartners)", output);
    }

    [Fact]
    public void UrlAssertion_UnknownExternalExpression_RemainsReviewTodo()
    {
        var model = CreateModel(new TestAction[]
        {
            new UrlAssertionAction(85, UrlAssertionKind.UrlEquals, "Urls.BaseUrlCatalogPartners")
        });

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("// await Expect(Page).ToHaveURLAsync(Urls.BaseUrlCatalogPartners)", output);
        Assert.Contains("EXTERNAL_URL_VARIABLE", output);
    }

    [Fact]
    public void AssertionLikeSuppression_EmitsFailingGuardInsteadOfEmptyGreenTest()
    {
        var model = CreateModel(new TestAction[]
        {
            new RawStatementAction(90, "currencyLabel.Text.Should(value)")
        }) with
        {
            SuppressedMethodPatterns = new[] { "*.*.Should(*)" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("ASSERTION_SUPPRESSION_BLOCKED", output);
        Assert.Contains("currencyLabel.Text.Should(value)", output);
        Assert.Contains("throw new NotImplementedException", output);
        Assert.DoesNotContain("source statement suppressed by adapter-config", output);
    }

    [Fact]
    public void BroadSuppressionPattern_CannotSilentlyHideAssertion()
    {
        var model = CreateModel(new TestAction[]
        {
            new RawStatementAction(91, "page.RowCostTable.Rows.First().StrategyValue.Text.Should().Be(value)")
        }) with
        {
            SuppressedMethodPatterns = new[] { "*page.RowCostTable.Rows*" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("ASSERTION_SUPPRESSION_BLOCKED", output);
        Assert.Contains("page.RowCostTable.Rows.First().StrategyValue.Text.Should().Be(value)", output);
        Assert.Contains("throw new NotImplementedException", output);
    }

    [Fact]
    public void EmptyTestAfterHarmlessSuppression_EmitsFailingGuard()
    {
        var model = CreateModel(new TestAction[]
        {
            new RawStatementAction(92, "page.WaitLoaded()")
        }) with
        {
            SuppressedMethodPatterns = new[] { "*page.WaitLoaded(*)" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("EMPTY_TEST_AFTER_SUPPRESSION", output);
        Assert.Contains("Assert.Inconclusive", output);
        Assert.DoesNotContain("throw new NotImplementedException", output);
    }

    [Fact]
    public void SuppressedBooleanDeclaration_IsNotBlockedByEmptyTestGuard()
    {
        var model = CreateModel(new TestAction[]
        {
            new LocalDeclarationAction(93, "element1", "var", "page.HasWarningAccept.Visible.Get()")
        }) with
        {
            SuppressedMethodPatterns = new[] { "*page.*.Visible.Get(*)" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("bool element1 = default", output);
        Assert.DoesNotContain("EMPTY_TEST_AFTER_SUPPRESSION", output);
    }

    [Fact]
    public void ResolvedRawStatement_AppendsSemicolonBeforeLineComment()
    {
        var model = CreateModel(new TestAction[]
        {
            new RawStatementAction(99, "await Expect(Page.Locator(\"[data-test=flag]\")).ToBeVisibleAsync()")
        });

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("await Expect(Page.Locator(\"[data-test=flag]\")).ToBeVisibleAsync(); // line 99", output);
        Assert.DoesNotContain("ToBeVisibleAsync() // line 99", output);
    }

    [Fact]
    public void VisibleGetLocalDeclaration_WithMappedTarget_RendersBooleanAssignmentForCondition()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new LocalDeclarationAction(100, "element1", "var", "page.HasWarningAccept.Visible.Get()"),
            new ConditionalBlockAction(
                101,
                "element1",
                new TestAction[] { new RawStatementAction(102, "Console.WriteLine(\"ok\")") },
                Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
                Array.Empty<TestAction>())
        });

        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.HasWarningAccept",
                    "[data-test=warning-accept]",
                    "CssSelector")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "page" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("var element1 = await Page.Locator(\"[data-test=warning-accept]\").IsVisibleAsync();", output);
        Assert.Contains("if (element1)", output);
        Assert.DoesNotContain("CONDITIONAL_UNRESOLVED_SYMBOL", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
    }

    [Fact]
    public void ExistsGetLocalDeclaration_WithMappedTarget_RendersBooleanAssignmentForCondition()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new LocalDeclarationAction(110, "exists", "var", "page.EmptyState.Exists.Get()"),
            new ConditionalBlockAction(
                111,
                "exists",
                new TestAction[] { new RawStatementAction(112, "Console.WriteLine(\"exists\")") },
                Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
                Array.Empty<TestAction>())
        });

        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.EmptyState",
                    "[data-test=empty-state]",
                    "CssSelector")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "page" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("var exists = await Page.Locator(\"[data-test=empty-state]\").CountAsync() > 0;", output);
        Assert.Contains("if (exists)", output);
        Assert.DoesNotContain("CONDITIONAL_UNRESOLVED_SYMBOL", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
    }

    [Fact]
    public void FluentTextAssertionRecognizer_HandlesGenericGetShouldBeChain()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;

public class SampleTests
{
    [Test]
    public void GeneratedTest()
    {
        page.ReportsSubtotalSalesAmount.Sum.Get().Should().Be(2988323.95m);
    }
}
");

            var model = new RoslynTestFileParser().Parse(file);
            var action = Assert.IsType<TextAssertionAction>(model.Tests.Single().BodyActions.Single());

            Assert.Equal("page.ReportsSubtotalSalesAmount.Sum", action.Target.SourceExpression);
            Assert.Equal(TextAssertionKind.TextEquals, action.Kind);
            Assert.Equal("2988323.95m", action.ExpectedValue);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void FluentTextAssertionRecognizer_HandlesTextGetReplaceShouldBeChain()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;

public class SampleTests
{
    [Test]
    public void GeneratedTest()
    {
        valueSum.ElementAt(1).Text().Get().Replace(""\u00a0"", "" "").Should().Be(""7 854 000,00 ₽"");
    }
}
");

            var model = new RoslynTestFileParser().Parse(file);
            var action = Assert.IsType<TextAssertionAction>(model.Tests.Single().BodyActions.Single());

            Assert.Equal("valueSum.ElementAt(1)", action.Target.SourceExpression);
            Assert.Equal(TextAssertionKind.TextEquals, action.Kind);
            Assert.Equal("\"7 854 000,00 ₽\"", action.ExpectedValue);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void WaitPolicy_ReceiverContains_AppliesToMatchingCustomWait()
    {
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            WaitPolicies: new[]
            {
                new WaitPolicyMapping
                {
                    MethodName = "Wait",
                    ReceiverContains = "TaskComplete",
                    Kind = "ProductStateVisible"
                }
            });
        var recognizer = new WaitInvocationRecognizer(RecognizerOptions.FromConfig(config));

        var action = recognizer.TryRecognize(new InvocationContext(
            "Wait",
            "page.Task.TaskComplete",
            "page.Task.TaskComplete.Wait(10000)",
            42,
            SymbolResolved: false,
            ArgumentTexts: new[] { "10000" }));

        var wait = Assert.IsType<WaitForAction>(action);
        Assert.Equal(WaitForKind.ProductStateVisible, wait.Kind);
        Assert.Equal("Wait", wait.SourceMethod);
    }

    [Fact]
    public void WaitPolicy_ReceiverContains_DoesNotApplyToOtherReceivers()
    {
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            WaitPolicies: new[]
            {
                new WaitPolicyMapping
                {
                    MethodName = "Wait",
                    ReceiverContains = "TaskComplete",
                    Kind = "ProductStateVisible"
                }
            });
        var recognizer = new WaitInvocationRecognizer(RecognizerOptions.FromConfig(config));

        var action = recognizer.TryRecognize(new InvocationContext(
            "Wait",
            "page.OtherControl",
            "page.OtherControl.Wait(10000)",
            42,
            SymbolResolved: false,
            ArgumentTexts: new[] { "10000" }));

        var wait = Assert.IsType<WaitForAction>(action);
        Assert.Equal(WaitForKind.ReviewRequired, wait.Kind);
    }

    [Fact]
    public void WaitPolicy_AdapterMapping_SkipsWaitRecognizerForMethodMappings()
    {
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            WaitPolicies: new[]
            {
                new WaitPolicyMapping
                {
                    MethodName = "WaitContainsText",
                    Kind = "AdapterMapping"
                }
            });
        var recognizer = new WaitInvocationRecognizer(RecognizerOptions.FromConfig(config));

        var action = recognizer.TryRecognize(new InvocationContext(
            "WaitContainsText",
            "page.Status",
            "page.Status.WaitContainsText(\"Ready\")",
            42,
            SymbolResolved: false,
            ArgumentTexts: new[] { "\"Ready\"" }));

        Assert.Null(action);
    }

    [Fact]
    public void WaitPolicy_SourceMethod_NormalizesGenericReceiverSyntax()
    {
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            WaitPolicies: new[]
            {
                new WaitPolicyMapping
                {
                    SourceMethod = "element.WaitDisabled()",
                    Kind = "Elide"
                }
            });
        var recognizer = new WaitInvocationRecognizer(RecognizerOptions.FromConfig(config));

        var action = recognizer.TryRecognize(new InvocationContext(
            "WaitDisabled",
            "discountSettingsPage.Save",
            "discountSettingsPage.Save.WaitDisabled()",
            42,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>()));

        var wait = Assert.IsType<WaitForAction>(action);
        Assert.Equal(WaitForKind.ActionabilityElided, wait.Kind);
    }


    [Fact]
    public void UnqualifiedHelperInvocation_ParsesAsStructuredMethodInvocation()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;

public class HelperTests
{
    [Test]
    public void T1()
    {
        CreateDopCalc(lightbox);
    }
}
");

            var model = new RoslynTestFileParser().Parse(file);
            var action = Assert.Single(model.Tests.Single().BodyActions);
            var helper = Assert.IsType<MethodInvocationAction>(action);

            Assert.Equal("CreateDopCalc", helper.MethodName);
            Assert.Equal(string.Empty, helper.ReceiverExpression);
            Assert.Equal("CreateDopCalc(lightbox)", helper.FullSourceText);
            Assert.Equal("lightbox", Assert.Single(helper.ArgumentTexts));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void UnqualifiedHelperInvocation_UsesParameterizedMapping()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;

public class HelperTests
{
    [Test]
    public void T1()
    {
        CreateDopCalc(lightbox);
    }
}
");

            var sourceModel = new RoslynTestFileParser().Parse(file);
            var config = new ProjectAdapterConfig(
                "sample",
                Array.Empty<UiTargetMapping>(),
                Array.Empty<PageObjectMapping>(),
                Array.Empty<MethodMapping>(),
                ParameterizedMethods: new[]
                {
                    new ParameterizedMethodMapping(
                        "CreateDopCalc({lightbox})",
                        new[] { "await CreateDopCalcAsync({lightbox});" },
                        requiresReview: false)
                });

            var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
            var output = new PlaywrightDotNetRenderer().Render(model);

            Assert.Contains("await CreateDopCalcAsync(lightbox);", output);
            Assert.DoesNotContain("HELPER_METHOD_REQUIRES_MAPPING", output);
            Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void UnqualifiedHelperInvocation_WithoutMapping_RendersHelperTodo()
    {
        var model = CreateModel(new TestAction[]
        {
            new MethodInvocationAction(
                10,
                string.Empty,
                "CreateDopCalc",
                "CreateDopCalc(lightbox)",
                new[] { "lightbox" },
                RecognitionConfidence.SyntaxFallback)
        });

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("HELPER_METHOD_REQUIRES_MAPPING", output);
        Assert.Contains("helper method requires mapping: CreateDopCalc", output);
        Assert.Contains("--mode helper-inventory", output);
        Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
    }


    [Fact]
    public void UnqualifiedGenericHelperInvocation_ParsesAsStructuredMethodInvocation()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;

public class HelperTests
{
    [Test]
    public void T1()
    {
        var page = GoToPageWithSupportUserAccessRight<ProjectsPage>(productId, AccessRight.View);
    }
}
");

            var model = new RoslynTestFileParser().Parse(file);
            var action = Assert.Single(model.Tests.Single().BodyActions);
            var helper = Assert.IsType<MethodInvocationAction>(action);

            Assert.Equal("GoToPageWithSupportUserAccessRight", helper.MethodName);
            Assert.Equal(string.Empty, helper.ReceiverExpression);
            Assert.Equal("page", helper.ResultVariable);
            Assert.Contains("<ProjectsPage>", helper.FullSourceText);
            Assert.Equal(new[] { "productId", "AccessRight.View" }, helper.ArgumentTexts);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void UnqualifiedGenericHelperInvocation_UsesParameterizedMapping()
    {
        var file = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(file, @"
using NUnit.Framework;

public class HelperTests
{
    [Test]
    public void T1()
    {
        var page = GoToPageWithSupportUserAccessRight<ProjectsPage>(productId, AccessRight.View);
    }
}
");

            var sourceModel = new RoslynTestFileParser().Parse(file);
            var config = new ProjectAdapterConfig(
                "sample",
                Array.Empty<UiTargetMapping>(),
                Array.Empty<PageObjectMapping>(),
                Array.Empty<MethodMapping>(),
                ParameterizedMethods: new[]
                {
                    new ParameterizedMethodMapping(
                        "GoToPageWithSupportUserAccessRight<{T}>({productId}, {right})",
                        new[] { "var {result} = await OpenWithSupportUserAccessRightAsync<{T}>({productId}, {right});" },
                        requiresReview: false)
                });

            var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
            var output = new PlaywrightDotNetRenderer().Render(model);

            Assert.Contains("var page = await OpenWithSupportUserAccessRightAsync<ProjectsPage>(productId, AccessRight.View);", output);
            Assert.DoesNotContain("HELPER_METHOD_REQUIRES_MAPPING", output);
            Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TargetSafeDeclaration_WithSourceOnlyArgument_RendersTodoInsteadOfActiveCode()
    {
        var model = CreateModel(new TestAction[]
        {
            new RawStatementAction(120, "var locator = Page.Locator(Urls.LegacySelector)")
        }) with
        {
            SourceOnlyIdentifiers = new[] { "Urls" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("SOURCE_ONLY_IN_STATEMENT", output);
        Assert.Contains("Urls.LegacySelector", output);
        Assert.DoesNotMatch(@"(?m)^\s*var\s+locator\s*=\s*Page\.Locator\(Urls\.LegacySelector\)", output);
    }

    [Fact]
    public void TrivialCommaRawFragment_IsNotRenderedAsActiveCodeEvenWhenSymbolsAreKnown()
    {
        var model = CreateModel(new TestAction[]
        {
            new RawStatementAction(121, "MarketerAccessRight.View,")
        }) with
        {
            TargetKnownTypes = new[] { "MarketerAccessRight" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("// source: MarketerAccessRight.View, // line 121", output);
        Assert.DoesNotMatch(@"(?m)^\s*MarketerAccessRight\.View,;\s*// line 121", output);
    }

    static TestFileModel CreateModel(IEnumerable<TestAction> actions) =>
        new(
            FilePath: "Sample.cs",
            Namespace: "Sample.Tests",
            ClassName: "SampleTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "GeneratedTest",
                    null,
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    actions)
            });
}
