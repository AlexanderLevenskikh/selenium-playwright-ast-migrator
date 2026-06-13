using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// Full inventory of a target Playwright .NET project, produced by TargetDiscovery.
/// All paths are relative to the project root.
/// </summary>
public sealed class TargetInventory
{
    [JsonPropertyName("ProjectRoot")]
    public string ProjectRoot { get; init; } = null!;

    [JsonPropertyName("ProjectFiles")]
    public IReadOnlyList<string> ProjectFiles { get; init; } = Array.Empty<string>();

    [JsonPropertyName("DetectedFrameworks")]
    public IReadOnlyList<DetectedFramework> DetectedFrameworks { get; init; } = Array.Empty<DetectedFramework>();

    [JsonPropertyName("DetectedTestHosts")]
    public IReadOnlyList<DetectedTestHost> DetectedTestHosts { get; init; } = Array.Empty<DetectedTestHost>();

    [JsonPropertyName("DetectedSetUpMethods")]
    public IReadOnlyList<DetectedSetUpMethod> DetectedSetUpMethods { get; init; } = Array.Empty<DetectedSetUpMethod>();

    [JsonPropertyName("DetectedTearDownMethods")]
    public IReadOnlyList<DetectedTearDownMethod> DetectedTearDownMethods { get; init; } = Array.Empty<DetectedTearDownMethod>();

    [JsonPropertyName("DetectedNavigationPatterns")]
    public IReadOnlyList<DetectedNavigationPattern> DetectedNavigationPatterns { get; init; } = Array.Empty<DetectedNavigationPattern>();

    [JsonPropertyName("DetectedAuthPatterns")]
    public IReadOnlyList<DetectedAuthPattern> DetectedAuthPatterns { get; init; } = Array.Empty<DetectedAuthPattern>();

    [JsonPropertyName("DetectedLocatorAttributes")]
    public IReadOnlyList<DetectedLocatorAttribute> DetectedLocatorAttributes { get; init; } = Array.Empty<DetectedLocatorAttribute>();

    [JsonPropertyName("DetectedLocatorMethods")]
    public IReadOnlyList<DetectedLocatorMethod> DetectedLocatorMethods { get; init; } = Array.Empty<DetectedLocatorMethod>();

    [JsonPropertyName("DetectedHelperMethods")]
    public IReadOnlyList<DetectedHelperMethod> DetectedHelperMethods { get; init; } = Array.Empty<DetectedHelperMethod>();

    [JsonPropertyName("DetectedNamespaces")]
    public IReadOnlyList<string> DetectedNamespaces { get; init; } = Array.Empty<string>();

    [JsonPropertyName("DetectedPlaywrightPatterns")]
    public IReadOnlyList<string> DetectedPlaywrightPatterns { get; init; } = Array.Empty<string>();

    [JsonPropertyName("DetectedUsings")]
    public IReadOnlyList<string> DetectedUsings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("Warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("RedactionCount")]
    public int RedactionCount { get; init; }
}

/// <summary>
/// A detected test framework with confidence and supporting evidence.
/// </summary>
public sealed class DetectedFramework
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("Confidence")]
    public string Confidence { get; init; } = null!;

    [JsonPropertyName("Evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public DetectedFramework() { }
    public DetectedFramework(string name, string confidence, IReadOnlyList<string> evidence)
    {
        Name = name;
        Confidence = confidence;
        Evidence = evidence;
    }
}

/// <summary>
/// A detected test host / base class candidate.
/// </summary>
public sealed class DetectedTestHost
{
    [JsonPropertyName("BaseClass")]
    public string BaseClass { get; init; } = null!;

    [JsonPropertyName("Namespace")]
    public string Namespace { get; init; } = null!;

    [JsonPropertyName("Framework")]
    public string? Framework { get; init; }

    [JsonPropertyName("ClassAttributes")]
    public IReadOnlyList<string> ClassAttributes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("Usings")]
    public IReadOnlyList<string> Usings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("Confidence")]
    public string Confidence { get; init; } = null!;

    [JsonPropertyName("Occurrences")]
    public int Occurrences { get; init; }

    [JsonPropertyName("Evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    [JsonPropertyName("File")]
    public string? File { get; init; }

    public DetectedTestHost() { }
    public DetectedTestHost(string baseClass, string @namespace, string? framework, IReadOnlyList<string> classAttributes, IReadOnlyList<string> usings, string confidence, int occurrences, IReadOnlyList<string> evidence, string? file = null)
    {
        BaseClass = baseClass;
        Namespace = @namespace;
        Framework = framework;
        ClassAttributes = classAttributes;
        Usings = usings;
        Confidence = confidence;
        Occurrences = occurrences;
        Evidence = evidence;
        File = file;
    }
}

/// <summary>
/// A detected [SetUp] or equivalent initialization method.
/// </summary>
public sealed class DetectedSetUpMethod
{
    [JsonPropertyName("ClassName")]
    public string ClassName { get; init; } = null!;

    [JsonPropertyName("MethodName")]
    public string MethodName { get; init; } = null!;

    [JsonPropertyName("Statements")]
    public IReadOnlyList<string> Statements { get; init; } = Array.Empty<string>();

    [JsonPropertyName("File")]
    public string? File { get; init; }

    public DetectedSetUpMethod() { }
    public DetectedSetUpMethod(string className, string methodName, IReadOnlyList<string> statements, string? file = null)
    {
        ClassName = className;
        MethodName = methodName;
        Statements = statements;
        File = file;
    }
}

/// <summary>
/// A detected [TearDown] or equivalent cleanup method.
/// </summary>
public sealed class DetectedTearDownMethod
{
    [JsonPropertyName("ClassName")]
    public string ClassName { get; init; } = null!;

    [JsonPropertyName("MethodName")]
    public string MethodName { get; init; } = null!;

    [JsonPropertyName("Statements")]
    public IReadOnlyList<string> Statements { get; init; } = Array.Empty<string>();

    [JsonPropertyName("File")]
    public string? File { get; init; }

    public DetectedTearDownMethod() { }
    public DetectedTearDownMethod(string className, string methodName, IReadOnlyList<string> statements, string? file = null)
    {
        ClassName = className;
        MethodName = methodName;
        Statements = statements;
        File = file;
    }
}

/// <summary>
/// A detected navigation pattern (GotoAsync, etc.).
/// </summary>
public sealed class DetectedNavigationPattern
{
    [JsonPropertyName("Pattern")]
    public string Pattern { get; init; } = null!;

    [JsonPropertyName("Example")]
    public string Example { get; init; } = null!;

    [JsonPropertyName("Evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public DetectedNavigationPattern() { }
    public DetectedNavigationPattern(string pattern, string example, IReadOnlyList<string> evidence)
    {
        Pattern = pattern;
        Example = example;
        Evidence = evidence;
    }
}

/// <summary>
/// A detected auth/login pattern.
/// </summary>
public sealed class DetectedAuthPattern
{
    [JsonPropertyName("Pattern")]
    public string Pattern { get; init; } = null!;

    [JsonPropertyName("Evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    [JsonPropertyName("SuggestedPlaceholder")]
    public string SuggestedPlaceholder { get; init; } = null!;

    public DetectedAuthPattern() { }
    public DetectedAuthPattern(string pattern, IReadOnlyList<string> evidence, string suggestedPlaceholder)
    {
        Pattern = pattern;
        Evidence = evidence;
        SuggestedPlaceholder = suggestedPlaceholder;
    }
}

/// <summary>
/// A detected locator attribute (data-testid, data-test-id, etc.).
/// </summary>
public sealed class DetectedLocatorAttribute
{
    [JsonPropertyName("Attribute")]
    public string Attribute { get; init; } = null!;

    [JsonPropertyName("Occurrences")]
    public int Occurrences { get; init; }

    [JsonPropertyName("Confidence")]
    public string Confidence { get; init; } = null!;

    public DetectedLocatorAttribute() { }
    public DetectedLocatorAttribute(string attribute, int occurrences, string confidence)
    {
        Attribute = attribute;
        Occurrences = occurrences;
        Confidence = confidence;
    }
}

/// <summary>
/// A detected locator method (GetByTestId, GetByRole, etc.).
/// </summary>
public sealed class DetectedLocatorMethod
{
    [JsonPropertyName("Method")]
    public string Method { get; init; } = null!;

    [JsonPropertyName("Occurrences")]
    public int Occurrences { get; init; }

    public DetectedLocatorMethod() { }
    public DetectedLocatorMethod(string method, int occurrences)
    {
        Method = method;
        Occurrences = occurrences;
    }
}

/// <summary>
/// A detected helper method (Login, GoTo..., Open..., etc.).
/// </summary>
public sealed class DetectedHelperMethod
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("Occurrences")]
    public int Occurrences { get; init; }

    [JsonPropertyName("Files")]
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

    [JsonPropertyName("PotentialUse")]
    public string PotentialUse { get; init; } = null!;

    [JsonPropertyName("RequiresReview")]
    public bool RequiresReview { get; init; } = true;

    public DetectedHelperMethod() { }
    public DetectedHelperMethod(string name, int occurrences, IReadOnlyList<string> files, string potentialUse)
    {
        Name = name;
        Occurrences = occurrences;
        Files = files;
        PotentialUse = potentialUse;
        RequiresReview = true;
    }
}
