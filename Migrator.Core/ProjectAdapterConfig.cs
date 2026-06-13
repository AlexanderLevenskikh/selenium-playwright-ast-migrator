using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// JSON-serializable config for mapping source expressions to Playwright targets.
/// Loaded from adapter project or config file, never contains project-specific logic.
/// </summary>
public sealed class ProjectAdapterConfig
{
    public string SourceProjectName { get; init; } = null!;
    public UiTargetMapping[] UiTargets { get; init; } = Array.Empty<UiTargetMapping>();
    public PageObjectMapping[] PageObjects { get; init; } = Array.Empty<PageObjectMapping>();
    public MethodMapping[] Methods { get; init; } = Array.Empty<MethodMapping>();

    /// <summary>
    /// Selector convention settings. When null, TestId mappings use default Page.GetByTestId().
    /// </summary>
    [JsonPropertyName("LocatorSettings")]
    public LocatorSettings? LocatorSettings { get; init; }

    /// <summary>
    /// Optional runtime host rendering settings. When present, the renderer generates
    /// a test class wrapper matching the target project's conventions (base class, attributes, usings, setup).
    /// </summary>
    [JsonPropertyName("TestHost")]
    public TestHostConfig? TestHost { get; init; }

    public ProjectAdapterConfig()
    {
    }

    public ProjectAdapterConfig(string SourceProjectName, UiTargetMapping[] UiTargets, PageObjectMapping[] PageObjects, MethodMapping[] Methods, LocatorSettings? LocatorSettings = null, TestHostConfig? TestHost = null)
    {
        this.SourceProjectName = SourceProjectName;
        this.UiTargets = UiTargets;
        this.PageObjects = PageObjects;
        this.Methods = Methods;
        this.LocatorSettings = LocatorSettings;
        this.TestHost = TestHost;
    }
}

/// <summary>
/// Selector convention settings — controls which HTML attribute TestId mappings use.
/// Lives only in config/profile, never in Core logic.
/// </summary>
public sealed class LocatorSettings
{
    /// <summary>
    /// Default HTML attribute for TestId mappings. When set, TestId targets render as
    /// <c>Page.Locator("[{attribute}='{value}']")</c> instead of <c>Page.GetByTestId("{value}")</c>.
    /// Default behavior (when null): uses <c>Page.GetByTestId()</c>.
    /// </summary>
    public string? DefaultTestIdAttribute { get; init; }

    /// <summary>
    /// Documentation-only list of known attributes used by the project.
    /// Not used for resolution logic.
    /// </summary>
    public string[]? KnownTestIdAttributes { get; init; }

    public LocatorSettings() { }
    public LocatorSettings(string? defaultTestIdAttribute, string[]? knownTestIdAttributes)
    {
        DefaultTestIdAttribute = defaultTestIdAttribute;
        KnownTestIdAttributes = knownTestIdAttributes;
    }
}

public sealed class UiTargetMapping
{
    public string SourceExpression { get; init; } = null!;
    public string TargetExpression { get; init; } = null!;
    public string TargetKind { get; init; } = null!;

    /// <summary>
    /// Per-mapping override of the HTML attribute for TestId targets.
    /// Overrides LocatorSettings.DefaultTestIdAttribute when set.
    /// Example: "data-tid", "data-test", "data-test-id".
    /// </summary>
    public string? TestIdAttribute { get; init; }

    /// <summary>
    /// Match strategy for selecting elements when multiple matches exist.
    /// Values: "First" (emits .First), "Nth" (emits .Nth(Index), requires Index).
    /// </summary>
    public string? Match { get; init; }

    /// <summary>
    /// Index for "Nth" match strategy. Ignored when Match is not "Nth".
    /// </summary>
    public int? Index { get; init; }

    public UiTargetMapping() { }
    public UiTargetMapping(string sourceExpression, string targetExpression, string targetKind, string? testIdAttribute = null, string? match = null, int? index = null)
    {
        SourceExpression = sourceExpression;
        TargetExpression = targetExpression;
        TargetKind = targetKind;
        TestIdAttribute = testIdAttribute;
        Match = match;
        Index = index;
    }
}

public sealed class PageObjectMapping
{
    public string SourceType { get; init; } = null!;
    public string TargetType { get; init; } = null!;
    public string VariableName { get; init; } = null!;
    public string ConstructorStrategy { get; init; } = null!;

    public PageObjectMapping() { }
    public PageObjectMapping(string sourceType, string targetType, string variableName, string constructorStrategy)
    {
        SourceType = sourceType;
        TargetType = targetType;
        VariableName = variableName;
        ConstructorStrategy = constructorStrategy;
    }
}

public sealed class MethodMapping
{
    public string SourceMethod { get; init; } = null!;
    public string? TargetMethod { get; init; }
    public string? Description { get; init; }
    public string[]? TargetStatements { get; init; }
    public bool RequiresReview { get; init; }

    public MethodMapping() { }
    public MethodMapping(string sourceMethod, string? targetMethod, string? description, string[]? targetStatements, bool requiresReview)
    {
        SourceMethod = sourceMethod;
        TargetMethod = targetMethod;
        Description = description;
        TargetStatements = targetStatements;
        RequiresReview = requiresReview;
    }
}

/// <summary>
/// Optional section in adapter config that controls how the generated test class
/// wraps into a real test host project (e.g. NUnit + TestBase + auth setup).
/// Lives in config/profile only — never hardcoded in Core/Roslyn/Renderer.
/// </summary>
public sealed class TestHostConfig
{
    /// <summary>
    /// Target namespace for generated file. Overrides source namespace when set.
    /// Example: "Example.E2ETests.Tests"
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Base class for generated test class. Defaults to "PageTest" when not set.
    /// Example: "TestBase"
    /// </summary>
    public string? BaseClass { get; init; }

    /// <summary>
    /// Additional C# attributes to place above the class declaration.
    /// Example: ["TestFixture", "Parallelizable(ParallelScope.Self)"]
    /// </summary>
    public string[]? ClassAttributes { get; init; }

    /// <summary>
    /// Additional using directives to prepend to the generated file.
    /// Example: ["NUnit.Framework", "Example.E2ETests.Infrastructure"]
    /// </summary>
    public string[]? Usings { get; init; }

    /// <summary>
    /// C# statements to render inside a [SetUp] method. When provided, replaces
    /// the original parsed setup actions (which are preserved as commented TODOs).
    /// Example: ["await Page.GotoAsync(DefaultEnvParams.TestLogin);", "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"]
    /// </summary>
    public string[]? SetUpStatements { get; init; }

    /// <summary>
    /// Class name override. When set, replaces the generated "{ClassName}Playwright" suffix.
    /// Example: "CatalogPrincipalsFilterPlaywrightTests"
    /// </summary>
    public string? ClassName { get; init; }
}
