using Migrator.Core.Models;
using Migrator.Roslyn;
using Migrator.Roslyn.Recognizers;

namespace Migrator.Tests;

public sealed class SemanticFrontendDeterminismTests
{
    [Fact]
    public void Mig03_DirectSeleniumIWebElementClick_ResolvesSemanticallyWithoutSeleniumPackageReference()
    {
        var model = Parse(
            """
            using OpenQA.Selenium;
            using NUnit.Framework;

            public class SemanticTests
            {
                [Test]
                public void Clicks()
                {
                    IWebElement element = default!;
                    element.Click();
                }
            }
            """);

        var click = Assert.Single(model.Tests.Single().BodyActions.OfType<ClickAction>());

        Assert.Equal(RecognitionConfidence.Semantic, click.Confidence);
        Assert.Equal("element", click.Target.SourceExpression);
    }

    [Fact]
    public void Mig03_ProjectOwnedClick_IsNegativeRecognition_NotSeleniumClick()
    {
        var model = Parse(
            """
            using NUnit.Framework;

            public sealed class Telemetry
            {
                public void Click() { }
            }

            public class SemanticTests
            {
                [Test]
                public void Tracks()
                {
                    var telemetry = new Telemetry();
                    telemetry.Click();
                }
            }
            """);

        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is ClickAction);
        var invocation = Assert.Single(actions.OfType<MethodInvocationAction>());
        Assert.Equal("telemetry", invocation.ReceiverExpression);
        Assert.Equal("Click", invocation.MethodName);
        Assert.Equal(RecognitionConfidence.Semantic, invocation.Confidence);
    }

    [Fact]
    public void Mig03_ProjectOwnedControlBaseName_DoesNotSpoofSeleniumControl()
    {
        var model = Parse(
            """
            using NUnit.Framework;

            public class ControlBase
            {
                public void Click() { }
            }

            public class SemanticTests
            {
                [Test]
                public void Clicks()
                {
                    var control = new ControlBase();
                    control.Click();
                }
            }
            """);

        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is ClickAction);
        Assert.Contains(actions, action =>
            action is MethodInvocationAction invocation &&
            invocation.MethodName == "Click" &&
            invocation.ReceiverExpression == "control");
    }

    [Fact]
    public void Mig03_ProjectAssertionHelperNamedThat_IsNotNUnitAssertion()
    {
        var model = Parse(
            """
            using NUnit.Framework;

            public sealed class ReportAssertions
            {
                public void That(object actual, object constraint) { }
            }

            public class SemanticTests
            {
                [Test]
                public void Checks()
                {
                    var helper = new ReportAssertions();
                    helper.That("actual", "constraint");
                }
            }
            """);

        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is AssertThatAction);
        Assert.Contains(actions, action =>
            action is MethodInvocationAction invocation &&
            invocation.MethodName == "That");
    }

    [Fact]
    public void Mig03_NUnitAssertThat_UsesSemanticAnchor()
    {
        var model = Parse(
            """
            using NUnit.Framework;

            public class SemanticTests
            {
                [Test]
                public void Checks()
                {
                    var actual = "ok";
                    Assert.That(actual, Is.EqualTo("ok"));
                }
            }
            """);

        var assertion = Assert.Single(model.Tests.Single().BodyActions.OfType<AssertThatAction>());

        Assert.Equal(RecognitionConfidence.Semantic, assertion.Confidence);
        Assert.Equal("actual", assertion.ActualExpression);
        Assert.Equal("Is.EqualTo(\"ok\")", assertion.ConstraintExpression);
    }

    [Fact]
    public void Mig03_ResolvedSystemMethod_IsSkippedBeforeSyntaxFallback()
    {
        var model = Parse(
            """
            using System.Collections.Generic;
            using NUnit.Framework;

            public class SemanticTests
            {
                [Test]
                public void Clears()
                {
                    var values = new List<int>();
                    values.Clear();
                }
            }
            """);

        Assert.DoesNotContain(
            model.Tests.Single().BodyActions,
            action => action is MethodInvocationAction invocation &&
                      invocation.MethodName == "Clear");
    }

    [Fact]
    public void Mig09_BuiltinRecognizerRegistrationOrder_DoesNotChangeSelectedAction()
    {
        var source = """
            using NUnit.Framework;

            public class SemanticTests
            {
                [Test]
                public void Clicks()
                {
                    page.Table.Items.ElementAt(2).Click();
                }
            }
            """;

        var forward = new RoslynTestFileParser(new List<IInvocationRecognizer>
        {
            new ClickInvocationRecognizer(),
            new TableInvocationRecognizer(),
            new PageObjectMethodRecognizer()
        });

        var reverse = new RoslynTestFileParser(new List<IInvocationRecognizer>
        {
            new PageObjectMethodRecognizer(),
            new TableInvocationRecognizer(),
            new ClickInvocationRecognizer()
        });

        var forwardAction = Parse(forward, source).Tests.Single().BodyActions.Single();
        var reverseAction = Parse(reverse, source).Tests.Single().BodyActions.Single();

        var forwardRow = Assert.IsType<TableRowAccessAction>(forwardAction);
        var reverseRow = Assert.IsType<TableRowAccessAction>(reverseAction);
        Assert.Equal(forwardRow.IndexExpression, reverseRow.IndexExpression);
        Assert.Equal("2", forwardRow.IndexExpression);
    }

    [Fact]
    public void Mig09_UnorderedCustomRecognizerCollision_IsStableAmbiguousBlocker()
    {
        var source = """
            using NUnit.Framework;

            public class SemanticTests
            {
                [Test]
                public void Runs()
                {
                    page.Do();
                }
            }
            """;

        var first = new RoslynTestFileParser(new List<IInvocationRecognizer>
        {
            new AlphaRecognizer(),
            new BetaRecognizer()
        });

        var second = new RoslynTestFileParser(new List<IInvocationRecognizer>
        {
            new BetaRecognizer(),
            new AlphaRecognizer()
        });

        var firstUnsupported = Assert.IsType<UnsupportedAction>(
            Parse(first, source).Tests.Single().BodyActions.Single());
        var secondUnsupported = Assert.IsType<UnsupportedAction>(
            Parse(second, source).Tests.Single().BodyActions.Single());

        Assert.Equal(firstUnsupported.Reason, secondUnsupported.Reason);
        Assert.Contains("AMBIGUOUS_RECOGNITION", firstUnsupported.Reason, StringComparison.Ordinal);
        Assert.Contains(nameof(AlphaRecognizer), firstUnsupported.Reason, StringComparison.Ordinal);
        Assert.Contains(nameof(BetaRecognizer), firstUnsupported.Reason, StringComparison.Ordinal);
    }

    static TestFileModel Parse(string source) =>
        Parse(new RoslynTestFileParser(), source);

    static TestFileModel Parse(RoslynTestFileParser parser, string source)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"migrator-semantic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "SemanticTests.cs");

        try
        {
            File.WriteAllText(path, source);
            return parser.Parse(path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    sealed class AlphaRecognizer : IInvocationRecognizer
    {
        public TestAction? TryRecognize(InvocationContext ctx) =>
            ctx.MethodName == "Do"
                ? new ClickAction(ctx.SourceLine, ctx.ReceiverText)
                : null;
    }

    sealed class BetaRecognizer : IInvocationRecognizer
    {
        public TestAction? TryRecognize(InvocationContext ctx) =>
            ctx.MethodName == "Do"
                ? new SendKeysAction(ctx.SourceLine, ctx.ReceiverText, "\"value\"")
                : null;
    }
}
