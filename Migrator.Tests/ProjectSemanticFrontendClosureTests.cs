using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Tests;

public sealed class ProjectSemanticFrontendClosureTests
{
    [Fact]
    public void Mig03_CrossFileProjectOwnedClick_IsSemanticNegativeForSeleniumClick()
    {
        using var fixture = new SemanticFixture();

        fixture.Write("Telemetry.cs",
            """
            public sealed class Telemetry
            {
                public void Click() { }
            }
            """);
        fixture.Write("SemanticTests.cs",
            """
            using NUnit.Framework;

            public sealed class SemanticTests
            {
                [Test]
                public void Tracks()
                {
                    var telemetry = new Telemetry();
                    telemetry.Click();
                }
            }
            """);

        var model = Assert.Single(new RoslynTestFileParser().ParseDirectory(fixture.Root));
        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is ClickAction);
        var invocation = Assert.Single(actions.OfType<MethodInvocationAction>());
        Assert.Equal("Click", invocation.MethodName);
        Assert.Equal(RecognitionConfidence.Semantic, invocation.Confidence);
    }

    [Fact]
    public void Mig03_CrossFileExtensionMethod_IsResolvedBySymbol_NotBySurfaceName()
    {
        using var fixture = new SemanticFixture();

        fixture.Write("Telemetry.cs",
            """
            public sealed class Telemetry { }

            public static class TelemetryExtensions
            {
                public static void Click(this Telemetry telemetry) { }
            }
            """);
        fixture.Write("SemanticTests.cs",
            """
            using NUnit.Framework;

            public sealed class SemanticTests
            {
                [Test]
                public void Tracks()
                {
                    var telemetry = new Telemetry();
                    telemetry.Click();
                }
            }
            """);

        var model = Assert.Single(new RoslynTestFileParser().ParseDirectory(fixture.Root));
        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is ClickAction);
        var invocation = Assert.Single(actions.OfType<MethodInvocationAction>());
        Assert.Equal(RecognitionConfidence.Semantic, invocation.Confidence);
    }

    [Fact]
    public void Mig03_InheritedHelperAcrossFiles_IsResolvedSemantically()
    {
        using var fixture = new SemanticFixture();

        fixture.Write("BaseFixture.cs",
            """
            public abstract class BaseFixture
            {
                protected void Click() { }
            }
            """);
        fixture.Write("SemanticTests.cs",
            """
            using NUnit.Framework;

            public sealed class SemanticTests : BaseFixture
            {
                [Test]
                public void Runs()
                {
                    Click();
                }
            }
            """);

        var model = Assert.Single(new RoslynTestFileParser().ParseDirectory(fixture.Root));
        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is ClickAction);
        var invocation = Assert.Single(actions.OfType<MethodInvocationAction>());
        Assert.Equal("Click", invocation.MethodName);
        Assert.Equal(RecognitionConfidence.Semantic, invocation.Confidence);
    }

    [Fact]
    public void Mig03_PartialClassHelperAcrossFiles_ParticipatesInSemanticCompilation()
    {
        using var fixture = new SemanticFixture();

        fixture.Write("SemanticTests.Helper.cs",
            """
            public sealed partial class SemanticTests
            {
                void ClickLocal() { }
            }
            """);
        fixture.Write("SemanticTests.cs",
            """
            using NUnit.Framework;

            public sealed partial class SemanticTests
            {
                [Test]
                public void Runs()
                {
                    ClickLocal();
                }
            }
            """);

        var model = Assert.Single(new RoslynTestFileParser().ParseDirectory(fixture.Root));
        var invocation = Assert.Single(
            model.Tests.Single().BodyActions.OfType<MethodInvocationAction>());

        Assert.Equal("ClickLocal", invocation.MethodName);
        Assert.Equal(RecognitionConfidence.Semantic, invocation.Confidence);
    }

    [Fact]
    public void Mig03_ProjectReferenceMethod_IsSourceOwnedSemanticCall()
    {
        using var fixture = new SemanticFixture();

        var helperDir = fixture.Directory("HelperLib");
        fixture.Write(Path.Combine("HelperLib", "HelperLib.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        fixture.Write(Path.Combine("HelperLib", "Telemetry.cs"),
            """
            namespace HelperLib;

            public sealed class Telemetry
            {
                public void Click() { }
            }
            """);

        var testsDir = fixture.Directory("Tests");
        fixture.Write(Path.Combine("Tests", "Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\HelperLib\HelperLib.csproj" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write(Path.Combine("Tests", "SemanticTests.cs"),
            """
            using HelperLib;
            using NUnit.Framework;

            public sealed class SemanticTests
            {
                [Test]
                public void Tracks()
                {
                    var telemetry = new Telemetry();
                    telemetry.Click();
                }
            }
            """);

        var model = Assert.Single(new RoslynTestFileParser().ParseDirectory(testsDir));
        var actions = model.Tests.Single().BodyActions.ToArray();

        Assert.DoesNotContain(actions, action => action is ClickAction);
        var invocation = Assert.Single(actions.OfType<MethodInvocationAction>());
        Assert.Equal("Click", invocation.MethodName);
        Assert.Equal(RecognitionConfidence.Semantic, invocation.Confidence);
    }

    [Fact]
    public void Mig03_FileCreationOrder_DoesNotChangeCrossFileSemanticClassification()
    {
        using var first = new SemanticFixture();
        using var second = new SemanticFixture();

        const string helper =
            """
            public sealed class Telemetry
            {
                public void Click() { }
            }
            """;
        const string test =
            """
            using NUnit.Framework;

            public sealed class SemanticTests
            {
                [Test]
                public void Tracks()
                {
                    var telemetry = new Telemetry();
                    telemetry.Click();
                }
            }
            """;

        first.Write("Telemetry.cs", helper);
        first.Write("SemanticTests.cs", test);

        second.Write("SemanticTests.cs", test);
        second.Write("Telemetry.cs", helper);

        var a = Assert.Single(new RoslynTestFileParser().ParseDirectory(first.Root));
        var b = Assert.Single(new RoslynTestFileParser().ParseDirectory(second.Root));

        var aAction = Assert.Single(
            a.Tests.Single().BodyActions.OfType<MethodInvocationAction>());
        var bAction = Assert.Single(
            b.Tests.Single().BodyActions.OfType<MethodInvocationAction>());

        Assert.Equal("Click", aAction.MethodName);
        Assert.Equal("Click", bAction.MethodName);
        Assert.Equal(RecognitionConfidence.Semantic, aAction.Confidence);
        Assert.Equal(RecognitionConfidence.Semantic, bAction.Confidence);
        Assert.Equal(aAction.Confidence, bAction.Confidence);
    }

    sealed class SemanticFixture : IDisposable
    {
        public SemanticFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "migrator-project-semantic-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Directory(string relative)
        {
            var path = Path.Combine(Root, relative);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public void Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative);
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
                System.IO.Directory.CreateDirectory(parent);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Root))
                System.IO.Directory.Delete(Root, recursive: true);
        }
    }
}


