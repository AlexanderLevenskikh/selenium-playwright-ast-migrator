using Migrator.Core;

namespace Migrator.Core.Models;

/// <summary>
/// Represents a parsed and (optionally) adapted test file.
/// </summary>
public sealed record TestFileModel
{
    public string FilePath { get; init; } = null!;
    public string Namespace { get; init; } = null!;
    public string ClassName { get; init; } = null!;
    public string? BaseClassName { get; init; }
    public IEnumerable<TestAction> SetUpActions { get; init; } = Array.Empty<TestAction>();
    public IEnumerable<TestModel> Tests { get; init; } = Array.Empty<TestModel>();

    /// <summary>
    /// Optional runtime host rendering settings. When set, the renderer uses these
    /// to emit project-specific class wrapper (usings, namespace, base class, attributes, setup).
    /// Set by the adapter from config, not by the parser.
    /// </summary>
    public TestHostConfig? TestHost { get; init; }

    /// <summary>
    /// Identifiers that exist only in the Selenium/source project and must not be
    /// emitted as active C# in generated Playwright tests.
    /// Set by the adapter from config.
    /// </summary>
    public IReadOnlyList<string> SourceOnlyIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Target-side type names that should be treated as available in generated code.
    /// Set by the adapter from config; used by renderer safety checks.
    /// </summary>
    public IReadOnlyList<string> TargetKnownTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Target-side identifiers that should be treated as available in generated code.
    /// Set by the adapter from config; used by renderer safety checks.
    /// </summary>
    public IReadOnlyList<string> TargetKnownIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Method names that should be rendered as source comments instead of active target code.
    /// Set by the adapter from config; used by renderer safety checks.
    /// </summary>
    public IReadOnlyList<string> SuppressedMethods { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Glob-like source method patterns that should be rendered as source comments before
    /// source-only/blocking safety checks.
    /// Set by the adapter from config; used by renderer safety checks.
    /// </summary>
    public IReadOnlyList<string> SuppressedMethodPatterns { get; init; } = Array.Empty<string>();

    public TestFileModel(
        string FilePath,
        string Namespace,
        string ClassName,
        string? BaseClassName,
        IEnumerable<TestAction> SetUpActions,
        IEnumerable<TestModel> Tests)
    {
        this.FilePath = FilePath;
        this.Namespace = Namespace;
        this.ClassName = ClassName;
        this.BaseClassName = BaseClassName;
        this.SetUpActions = SetUpActions ?? Array.Empty<TestAction>();
        this.Tests = Tests ?? Array.Empty<TestModel>();
    }
}
