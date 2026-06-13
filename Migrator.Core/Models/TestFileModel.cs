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
