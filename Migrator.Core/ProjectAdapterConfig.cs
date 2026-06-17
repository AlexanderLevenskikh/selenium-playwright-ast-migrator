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
    /// Target-side type names that are valid in generated Playwright code.
    /// Use this for project/domain enums and static helper types such as Product or Navigation.
    /// These values live in adapter config/profile, not in the renderer.
    /// </summary>
    [JsonPropertyName("TargetKnownTypes")]
    public string[] TargetKnownTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Target-side identifiers that are valid in generated Playwright code but are not
    /// framework built-ins. Use this sparingly for configured target helpers.
    /// </summary>
    [JsonPropertyName("TargetKnownIdentifiers")]
    public string[] TargetKnownIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Identifiers that are available in the Selenium/source project but not in the
    /// target Playwright project. Statements that reference them are rendered as
    /// safe TODO comments instead of active C#.
    /// </summary>
    [JsonPropertyName("SourceOnlyIdentifiers")]
    public string[] SourceOnlyIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Parameterized method mappings with placeholder support.
    /// Pattern uses {placeholderName} syntax. Priority: exact SourceMethod wins over SourceMethodPattern.
    /// </summary>
    [JsonPropertyName("ParameterizedMethods")]
    public ParameterizedMethodMapping[] ParameterizedMethods { get; init; } = Array.Empty<ParameterizedMethodMapping>();

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

    /// <summary>
    /// Optional per-file/per-suite scopes. Each scope can override TestHost, UiTargets, and Methods
    /// for source files matching SourcePathPatterns. Global mappings serve as base.
    /// </summary>
    [JsonPropertyName("Scopes")]
    public ProfileScope[] Scopes { get; init; } = Array.Empty<ProfileScope>();


    /// <summary>
    /// Optional project-aware verification settings. Used by CLI mode verify-project to
    /// compile generated Playwright files in a temporary .csproj with real project/package references.
    /// This does not modify the source project.
    /// </summary>
    [JsonPropertyName("Verification")]
    public VerificationConfig? Verification { get; init; }

    /// <summary>
    /// Optional quality gate settings for verify mode. Controls thresholds and fail conditions.
    /// When absent, soft defaults are used (warnings reported, but verify does not fail unexpectedly).
    /// </summary>
    [JsonPropertyName("QualityGates")]
    public QualityGatesConfig? QualityGates { get; init; }

    /// <summary>
    /// Optional table/list mappings. Maps source expressions like "page.Table" to row targets
    /// for ElementAt(N) and count access patterns. Config-driven, no project-specific hardcode.
    /// </summary>
    [JsonPropertyName("Tables")]
    public TableConfig[] Tables { get; init; } = Array.Empty<TableConfig>();

    /// <summary>
    /// Optional pagination mappings. Maps source expressions like "page.Pagination.Forward"
    /// to Playwright targets for navigation actions.
    /// </summary>
    [JsonPropertyName("Pagination")]
    public PaginationConfig[] Pagination { get; init; } = Array.Empty<PaginationConfig>();

    public ProjectAdapterConfig()
    {
    }

    public ProjectAdapterConfig(string SourceProjectName, UiTargetMapping[] UiTargets, PageObjectMapping[] PageObjects, MethodMapping[] Methods, LocatorSettings? LocatorSettings = null, TestHostConfig? TestHost = null, ParameterizedMethodMapping[]? ParameterizedMethods = null, ProfileScope[]? Scopes = null, QualityGatesConfig? QualityGates = null, TableConfig[]? Tables = null, PaginationConfig[]? Pagination = null, string[]? SourceOnlyIdentifiers = null, string[]? TargetKnownTypes = null, string[]? TargetKnownIdentifiers = null, VerificationConfig? Verification = null)
    {
        this.SourceProjectName = SourceProjectName;
        this.UiTargets = UiTargets;
        this.PageObjects = PageObjects;
        this.Methods = Methods;
        this.LocatorSettings = LocatorSettings;
        this.TestHost = TestHost;
        this.ParameterizedMethods = ParameterizedMethods ?? Array.Empty<ParameterizedMethodMapping>();
        this.Scopes = Scopes ?? Array.Empty<ProfileScope>();
        this.QualityGates = QualityGates;
        this.Tables = Tables ?? Array.Empty<TableConfig>();
        this.Pagination = Pagination ?? Array.Empty<PaginationConfig>();
        this.SourceOnlyIdentifiers = SourceOnlyIdentifiers ?? Array.Empty<string>();
        this.TargetKnownTypes = TargetKnownTypes ?? Array.Empty<string>();
        this.TargetKnownIdentifiers = TargetKnownIdentifiers ?? Array.Empty<string>();
        this.Verification = Verification;
    }
}

/// <summary>
/// Project-aware verification settings for CLI mode verify-project.
/// The migrator creates a temporary .csproj under the output directory, includes generated files,
/// adds the configured references, and runs dotnet build. The source project is never modified.
/// </summary>
public sealed class VerificationConfig
{
    /// <summary>
    /// Target framework for the temporary verification project. Default: net8.0.
    /// </summary>
    public string? TargetFramework { get; init; }

    /// <summary>
    /// Optional base directory for resolving relative project/reference paths.
    /// When absent, paths are resolved relative to the adapter config file directory, then current working directory.
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>
    /// Project references to add to the temporary verification project.
    /// Use this for test infrastructure projects such as ArBilling.Infrastructure or the source test csproj.
    /// </summary>
    public string[] ProjectReferences { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Package references needed by generated tests. Microsoft.Playwright.NUnit and NUnit are added by default
    /// unless DisableDefaultPackageReferences is true.
    /// </summary>
    public PackageReferenceConfig[] PackageReferences { get; init; } = Array.Empty<PackageReferenceConfig>();

    /// <summary>
    /// Raw assembly references. Use only when ProjectReference/PackageReference is not possible.
    /// </summary>
    public string[] AssemblyReferences { get; init; } = Array.Empty<string>();

    /// <summary>
    /// If true, do not add default Playwright/NUnit packages to the temporary project.
    /// </summary>
    public bool? DisableDefaultPackageReferences { get; init; }

    /// <summary>
    /// If true, verify-project tries to find the nearest .csproj upward from --input and add it as a ProjectReference
    /// when no ProjectReferences are configured. Default: true.
    /// </summary>
    public bool? AutoDiscoverNearestProject { get; init; }

    /// <summary>
    /// If true, dotnet build runs with --no-restore. Default: false.
    /// </summary>
    public bool? NoRestore { get; init; }

    /// <summary>
    /// Build configuration for dotnet build. Default: Debug.
    /// </summary>
    public string? Configuration { get; init; }

    /// <summary>
    /// Optional runtime identifier passed to dotnet build. Usually null.
    /// </summary>
    public string? RuntimeIdentifier { get; init; }

    public VerificationConfig() { }
}

public sealed class PackageReferenceConfig
{
    public string Include { get; init; } = null!;
    public string Version { get; init; } = null!;

    public PackageReferenceConfig() { }
}

/// <summary>
/// Quality gate settings for verify mode. All fields are optional — soft defaults apply when not set.
/// </summary>
public sealed class QualityGatesConfig
{
    /// <summary>
    /// Maximum allowed TODO comments across all generated files.
    /// Default: int.MaxValue (soft — warnings only).
    /// </summary>
    public int? MaxTodoComments { get; init; }

    /// <summary>
    /// Maximum allowed unsupported actions.
    /// Default: int.MaxValue (soft — warnings only).
    /// </summary>
    public int? MaxUnsupportedActions { get; init; }

    /// <summary>
    /// Maximum allowed unmapped targets.
    /// Default: int.MaxValue (soft — warnings only).
    /// </summary>
    public int? MaxUnmappedTargets { get; init; }

    /// <summary>
    /// Maximum allowed raw expressions.
    /// Default: int.MaxValue (soft — warnings only).
    /// </summary>
    public int? MaxRawExpressions { get; init; }

    /// <summary>
    /// Fail verify if any Page.TODO_* calls remain in generated code.
    /// Default: true.
    /// </summary>
    public bool? FailOnPageTodo { get; init; }

    /// <summary>
    /// Fail verify if generated code has C# syntax errors.
    /// Default: true.
    /// </summary>
    public bool? FailOnInvalidGeneratedSyntax { get; init; }

    /// <summary>
    /// Fail verify if multiple scopes match a single source file.
    /// Default: true.
    /// </summary>
    public bool? FailOnMultipleMatchingScopes { get; init; }

    /// <summary>
    /// Fail verify if unresolved placeholder tokens remain in generated output.
    /// Default: true.
    /// </summary>
    public bool? FailOnPlaceholderLeftovers { get; init; }

    /// <summary>
    /// Fail verify if suspicious literal variable names are found in selectors.
    /// Default: true.
    /// </summary>
    public bool? FailOnSuspiciousLiteralVariables { get; init; }

    /// <summary>
    /// Fail verify if local/private config values are detected in public profiles.
    /// Default: true.
    /// </summary>
    public bool? FailOnLocalProfileLeaks { get; init; }

    public QualityGatesConfig() { }
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
    /// Example: ["await Page.GotoAsync(\"<test-login>\");", "await Page.GotoAsync(\"/catalogs\");"]
    /// </summary>
    public string[]? SetUpStatements { get; init; }

    /// <summary>
    /// Class name override. When set, replaces the generated "{ClassName}Playwright" suffix.
    /// Example: "CatalogPrincipalsFilterPlaywrightTests"
    /// </summary>
    public string? ClassName { get; init; }
}

/// <summary>
/// A parameterized method mapping with placeholder support.
/// SourceMethodPattern uses {placeholderName} syntax to match method arguments.
/// Placeholders are replaced in TargetStatements with the actual argument text.
/// Exact SourceMethod mapping (MethodMapping) has priority over SourceMethodPattern.
/// </summary>
public sealed class ParameterizedMethodMapping
{
    /// <summary>
    /// Pattern to match against method invocation text (without trailing semicolon).
    /// Use {placeholderName} for arguments. Example: "page.NameSort.Sort({sortOrder})"
    /// The pattern must match receiver.method(argument(s)).
    /// </summary>
    [JsonPropertyName("SourceMethodPattern")]
    public string SourceMethodPattern { get; init; } = null!;

    /// <summary>
    /// Target statements with placeholders replaced by actual argument values.
    /// </summary>
    public string[]? TargetStatements { get; init; }

    /// <summary>
    /// Whether the mapping requires manual review after generation.
    /// </summary>
    public bool RequiresReview { get; init; }

    /// <summary>
    /// Optional description for documentation purposes.
    /// </summary>
    public string? Description { get; init; }

    public ParameterizedMethodMapping() { }
    public ParameterizedMethodMapping(string sourceMethodPattern, string[]? targetStatements, bool requiresReview, string? description = null)
    {
        SourceMethodPattern = sourceMethodPattern;
        TargetStatements = targetStatements;
        RequiresReview = requiresReview;
        Description = description;
    }
}

/// <summary>
/// A scoped override for a subset of source files. Allows per-file/per-suite
/// configuration of TestHost, UiTargets, Methods, and ParameterizedMethods.
/// Global config serves as base; scope entries override or extend.
/// </summary>
public sealed class ProfileScope
{
    /// <summary>
    /// Human-readable scope name for logging and reports.
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// Glob-like path patterns to match source files. Supports **/* wildcards.
    /// Example: "**/CatalogPrincipalsFilter.cs"
    /// </summary>
    [JsonPropertyName("SourcePathPatterns")]
    public string[] SourcePathPatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Overrides global TestHost for files matching this scope.
    /// When null, global TestHost is used.
    /// </summary>
    [JsonPropertyName("TestHost")]
    public TestHostConfig? TestHost { get; init; }

    /// <summary>
    /// Scope-specific UiTargets. Merged with global UiTargets.
    /// Scope entries override global entries for the same SourceExpression.
    /// </summary>
    [JsonPropertyName("UiTargets")]
    public UiTargetMapping[] UiTargets { get; init; } = Array.Empty<UiTargetMapping>();

    /// <summary>
    /// Scope-specific exact method mappings. Merged with global Methods.
    /// Scope entries override global entries for the same SourceMethod.
    /// </summary>
    [JsonPropertyName("Methods")]
    public MethodMapping[] Methods { get; init; } = Array.Empty<MethodMapping>();

    /// <summary>
    /// Scope-specific parameterized method mappings. Merged with global ParameterizedMethods.
    /// Scope entries override global entries for the same SourceMethodPattern.
    /// </summary>
    [JsonPropertyName("ParameterizedMethods")]
    public ParameterizedMethodMapping[] ParameterizedMethods { get; init; } = Array.Empty<ParameterizedMethodMapping>();

    /// <summary>
    /// Scope-specific target-known type additions. Unioned with global TargetKnownTypes.
    /// </summary>
    [JsonPropertyName("TargetKnownTypes")]
    public string[] TargetKnownTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Scope-specific target-known identifier additions. Unioned with global TargetKnownIdentifiers.
    /// </summary>
    [JsonPropertyName("TargetKnownIdentifiers")]
    public string[] TargetKnownIdentifiers { get; init; } = Array.Empty<string>();

    public ProfileScope() { }
    public ProfileScope(string name, string[] sourcePathPatterns, TestHostConfig? testHost = null,
        UiTargetMapping[]? uiTargets = null, MethodMapping[]? methods = null,
        ParameterizedMethodMapping[]? parameterizedMethods = null,
        string[]? targetKnownTypes = null, string[]? targetKnownIdentifiers = null)
    {
        Name = name;
        SourcePathPatterns = sourcePathPatterns;
        TestHost = testHost;
        UiTargets = uiTargets ?? Array.Empty<UiTargetMapping>();
        Methods = methods ?? Array.Empty<MethodMapping>();
        ParameterizedMethods = parameterizedMethods ?? Array.Empty<ParameterizedMethodMapping>();
        TargetKnownTypes = targetKnownTypes ?? Array.Empty<string>();
        TargetKnownIdentifiers = targetKnownIdentifiers ?? Array.Empty<string>();
    }

    /// <summary>
    /// Scope-specific table mappings. Merged with global Tables.
    /// Scope entries override global entries for the same SourceExpression.
    /// </summary>
    [JsonPropertyName("Tables")]
    public TableConfig[] Tables { get; init; } = Array.Empty<TableConfig>();

    /// <summary>
    /// Scope-specific pagination mappings. Merged with global Pagination.
    /// Scope entries override global entries for the same SourceExpression.
    /// </summary>
    [JsonPropertyName("Pagination")]
    public PaginationConfig[] Pagination { get; init; } = Array.Empty<PaginationConfig>();
}

/// <summary>
/// Table/list mapping config. Maps a source expression (e.g. "page.Table") to a row target
/// for ElementAt(N) access, row text assertions, and table count checks.
/// Config-driven — no project-specific selectors hardcoded in Core/Roslyn/Renderer.
/// </summary>
public sealed class TableConfig
{
    /// <summary>
    /// Source expression that identifies this table (e.g. "page.Table").
    /// Used to match ElementAt/indexer/count access patterns.
    /// </summary>
    [JsonPropertyName("SourceExpression")]
    public string SourceExpression { get; init; } = null!;

    /// <summary>
    /// Target expression for individual table rows. Used for ElementAt(N) → .Nth(N) mapping.
    /// </summary>
    [JsonPropertyName("RowTarget")]
    public TargetMappingEntry? RowTarget { get; init; }

    /// <summary>
    /// Optional pagination config embedded in table mapping.
    /// </summary>
    [JsonPropertyName("Pagination")]
    public PaginationMappingEntry? Pagination { get; init; }

    public TableConfig() { }
}

/// <summary>
/// Target mapping entry for a table row or pagination element.
/// Supports existing TargetKind values: TestId, Text, RawExpression (last resort).
/// </summary>
public sealed class TargetMappingEntry
{
    /// <summary>
    /// Target expression (e.g. "t_table_row_item").
    /// For TestId kind, this is the attribute value (e.g. the value of data-test="t_table_row_item").
    /// </summary>
    [JsonPropertyName("TargetExpression")]
    public string TargetExpression { get; init; } = null!;

    /// <summary>
    /// Target kind: "TestId", "Text", or "RawExpression".
    /// "TestId" is recommended — renders as Page.Locator("[{TestIdAttribute}='{TargetExpression}']").
    /// </summary>
    [JsonPropertyName("TargetKind")]
    public string TargetKind { get; init; } = "TestId";

    /// <summary>
    /// HTML attribute name for TestId targets. Overrides LocatorSettings.DefaultTestIdAttribute when set.
    /// Examples: "data-test", "data-tid", "data-test-id".
    /// </summary>
    [JsonPropertyName("TestIdAttribute")]
    public string? TestIdAttribute { get; init; }

    public TargetMappingEntry() { }
}

/// <summary>
/// Pagination mapping for embedded table pagination.
/// </summary>
public sealed class PaginationMappingEntry
{
    /// <summary>
    /// Target expression for the forward (next page) button.
    /// </summary>
    [JsonPropertyName("Forward")]
    public TargetMappingEntry? Forward { get; init; }

    public PaginationMappingEntry() { }
}

/// <summary>
/// Pagination config at global/profile level. Maps source expressions like
/// "page.Pagination.Forward" to Playwright targets for click actions.
/// </summary>
public sealed class PaginationConfig
{
    /// <summary>
    /// Source expression to match (e.g. "page.Pagination.Forward").
    /// </summary>
    [JsonPropertyName("SourceExpression")]
    public string SourceExpression { get; init; } = null!;

    /// <summary>
    /// Target expression for the pagination element.
    /// </summary>
    [JsonPropertyName("TargetExpression")]
    public string TargetExpression { get; init; } = null!;

    /// <summary>
    /// Target kind: "TestId", "Text", or "RawExpression".
    /// </summary>
    [JsonPropertyName("TargetKind")]
    public string TargetKind { get; init; } = "TestId";

    /// <summary>
    /// HTML attribute name for TestId targets.
    /// </summary>
    [JsonPropertyName("TestIdAttribute")]
    public string? TestIdAttribute { get; init; }

    public PaginationConfig() { }
}
