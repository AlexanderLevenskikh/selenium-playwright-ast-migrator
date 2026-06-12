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

    public ProjectAdapterConfig()
    {
    }

    public ProjectAdapterConfig(string SourceProjectName, UiTargetMapping[] UiTargets, PageObjectMapping[] PageObjects, MethodMapping[] Methods, LocatorSettings? LocatorSettings = null)
    {
        this.SourceProjectName = SourceProjectName;
        this.UiTargets = UiTargets;
        this.PageObjects = PageObjects;
        this.Methods = Methods;
        this.LocatorSettings = LocatorSettings;
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

    public UiTargetMapping() { }
    public UiTargetMapping(string sourceExpression, string targetExpression, string targetKind, string? testIdAttribute = null)
    {
        SourceExpression = sourceExpression;
        TargetExpression = targetExpression;
        TargetKind = targetKind;
        TestIdAttribute = testIdAttribute;
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
