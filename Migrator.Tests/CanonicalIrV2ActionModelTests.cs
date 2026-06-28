using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;

namespace Migrator.Tests;

/// <summary>
/// PROD-04 guard for the IR V2 canonical action model.
/// These tests keep new source/target work from falling back to generic Raw/Unsupported nodes.
/// </summary>
public class CanonicalIrV2ActionModelTests
{
    [Fact]
    public void LegacyBridge_MapsCommonLegacyActionsToCanonicalV2Statements()
    {
        var model = CreateCanonicalModel();
        var document = LegacyIrBridge.ToDocument(model, target: new TargetSpec("playwright-dotnet", "csharp", "playwright"));
        var body = document.Suite.Tests.Single().Body;

        Assert.Collection(
            body,
            action => Assert.IsType<ClickStatementIr>(action),
            action => Assert.IsType<FillStatementIr>(action),
            action => Assert.IsType<PressStatementIr>(action),
            action => Assert.IsType<DeclarationStatementIr>(action),
            action => Assert.IsType<LocatorDeclarationStatementIr>(action),
            action => Assert.IsType<MethodInvocationStatementIr>(action),
            action => Assert.IsType<MappedMethodStatementIr>(action),
            action => Assert.IsType<MappedExpressionAssertionStatementIr>(action),
            action => Assert.IsType<AssertAreEqualStatementIr>(action),
            action => Assert.IsType<AssertThatStatementIr>(action),
            action => Assert.IsType<AssertMultipleStatementIr>(action),
            action => Assert.IsType<TableCountAssertionStatementIr>(action),
            action => Assert.IsType<TableRowAccessStatementIr>(action),
            action => Assert.IsType<TableRowTextAccessStatementIr>(action),
            action => Assert.IsType<ConditionalBlockStatementIr>(action));

        var mapped = Assert.IsType<MappedMethodStatementIr>(body[6]);
        Assert.Equal("WaitVisible", mapped.SourceMethod);
        Assert.Equal("target", mapped.ResultVariable);
        Assert.NotNull(mapped.Target);
        Assert.True(mapped.TargetStatementsByTarget.ContainsKey("playwright-typescript"));

        var conditional = Assert.IsType<ConditionalBlockStatementIr>(body[^1]);
        Assert.Single(conditional.IfStatements);
        Assert.Single(conditional.ElseIfBranches);
        Assert.Single(conditional.ElseStatements);
    }

    [Fact]
    public void LegacyBridge_RoundtripPreservesCanonicalActionShapes()
    {
        var model = CreateCanonicalModel();
        var document = LegacyIrBridge.ToDocument(model, target: new TargetSpec("playwright-dotnet", "csharp", "playwright"));
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);
        var actions = lowered.Tests.Single().BodyActions.ToList();

        Assert.IsType<ClickAction>(actions[0]);
        Assert.IsType<SendKeysAction>(actions[1]);
        Assert.IsType<PressAction>(actions[2]);
        Assert.IsType<LocalDeclarationAction>(actions[3]);
        Assert.IsType<LocatorDeclarationAction>(actions[4]);
        Assert.IsType<MethodInvocationAction>(actions[5]);
        Assert.IsType<MappedMethodInvocationAction>(actions[6]);
        Assert.IsType<MappedExpressionAssertionAction>(actions[7]);
        Assert.IsType<AssertAreEqualAction>(actions[8]);
        Assert.IsType<AssertThatAction>(actions[9]);
        Assert.IsType<AssertMultipleAction>(actions[10]);
        Assert.IsType<TableCountAssertionAction>(actions[11]);
        Assert.IsType<TableRowAccessAction>(actions[12]);
        Assert.IsType<TableRowTextAccessAction>(actions[13]);
        Assert.IsType<ConditionalBlockAction>(actions[14]);

        var mapped = Assert.IsType<MappedMethodInvocationAction>(actions[6]);
        Assert.Equal("WaitVisible", mapped.SourceMethod);
        Assert.Equal("target", mapped.ResultVariable);
        Assert.True(mapped.TargetStatementsByTarget.ContainsKey("playwright-typescript"));

        var conditional = Assert.IsType<ConditionalBlockAction>(actions[14]);
        Assert.Single(conditional.IfActions);
        Assert.Single(conditional.ElseIfActions);
        Assert.Single(conditional.ElseActions);
    }

    [Fact]
    public void V2Dump_IncludesCanonicalActionKinds()
    {
        var sourceModel = CreateCanonicalModel();
        var result = new PipelineResult(
            SourceModel: sourceModel,
            TargetModel: sourceModel,
            GeneratedOutput: string.Empty,
            Report: ReportBuilder.Build(sourceModel, string.Empty));

        var dump = V2IrDumpWriter.Build(new[] { result }, new TargetSpec("playwright-dotnet", "csharp", "playwright"));
        var json = V2IrDumpWriter.ToJson(dump);

        Assert.Contains("\"Kind\": \"Press\"", json);
        Assert.Contains("\"Kind\": \"Declaration\"", json);
        Assert.Contains("\"Kind\": \"LocatorDeclaration\"", json);
        Assert.Contains("\"Kind\": \"MappedMethod\"", json);
        Assert.Contains("\"Kind\": \"MappedExpressionAssertion\"", json);
        Assert.Contains("\"Kind\": \"TableCountAssertion\"", json);
        Assert.Contains("\"Kind\": \"ConditionalBlock\"", json);
    }

    static TestFileModel CreateCanonicalModel() =>
        new(
            FilePath: "/repo/CanonicalTests.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "CanonicalTests",
            BaseClassName: "BaseUiTest",
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "CanonicalActions",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("page.Save", "save", TargetKind.TestIdBeginning, "data-tid")),
                        new SendKeysAction(11, TargetExpression.Mapped("page.Name", "#name", TargetKind.CssSelector), "userName"),
                        new PressAction(12, TargetExpression.Mapped("page.Search", "#search", TargetKind.CssSelector), "Enter"),
                        new LocalDeclarationAction(13, "code", "var", "GetCode()"),
                        new LocatorDeclarationAction(14, "row", "Page.Locator(\".row\")", "var row = Driver.FindElement(By.CssSelector(\".row\"));"),
                        new MethodInvocationAction(15, "helper", "Refresh", "helper.Refresh(productId);", new[] { "productId" }, resultVariable: null),
                        new MappedMethodInvocationAction(
                            16,
                            "target.WaitVisible();",
                            new[] { "await Assertions.Expect({TARGET}).ToBeVisibleAsync();" },
                            targetExpr: TargetExpression.Mapped("target", "#target", TargetKind.CssSelector),
                            sourceMethod: "WaitVisible",
                            resultVariable: "target",
                            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["playwright-typescript"] = new[] { "await expect({TARGET}).toBeVisible();" }
                            }),
                        new MappedExpressionAssertionAction(
                            17,
                            "target.Get().Should().Be(expected);",
                            "await Assertions.Expect({TARGET}).ToHaveTextAsync(expected);",
                            targetExpr: TargetExpression.Mapped("target", "#target", TargetKind.CssSelector),
                            sourceMethod: "ShouldBe"),
                        new AssertAreEqualAction(18, "expected", "actual"),
                        new AssertThatAction(19, "actual", "Is.Not.Null"),
                        new AssertMultipleAction(20, "Assert.Multiple(() => { ... });", new TestAction[]
                        {
                            new VisibilityAssertionAction(21, TargetExpression.Mapped("page.Loader", ".loader", TargetKind.CssSelector), VisibilityKind.Hidden)
                        }),
                        new TableCountAssertionAction(22, TargetExpression.Mapped("page.Rows", ".row", TargetKind.CssSelector), TableCountKind.CountGreaterThanZero, null, "page.Rows.Count.Should().BeGreaterThan(0);"),
                        new TableRowAccessAction(23, TargetExpression.Mapped("page.Rows", ".row", TargetKind.CssSelector), "index", "page.Rows.ElementAt(index);"),
                        new TableRowTextAccessAction(24, TargetExpression.Mapped("page.Rows", ".row", TargetKind.CssSelector), "0", "page.Rows.ElementAt(0).Text.Get();"),
                        new ConditionalBlockAction(
                            25,
                            "hasItems",
                            new TestAction[] { new ClickAction(26, TargetExpression.Mapped("page.First", ".first", TargetKind.CssSelector)) },
                            new[]
                            {
                                ("hasFallback", (IReadOnlyList<TestAction>)new TestAction[]
                                {
                                    new ClickAction(27, TargetExpression.Mapped("page.Fallback", ".fallback", TargetKind.CssSelector))
                                })
                            },
                            new TestAction[] { new RawStatementAction(28, "Console.WriteLine(\"empty\");") })
                    })
            });
}
