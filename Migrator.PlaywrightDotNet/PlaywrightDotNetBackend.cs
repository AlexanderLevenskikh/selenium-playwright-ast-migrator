using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;

namespace Migrator.PlaywrightDotNet;

/// <summary>
/// Built-in target backend for Playwright .NET/NUnit output.
/// </summary>
public sealed class PlaywrightDotNetBackend : ITargetBackend
{
    readonly PlaywrightDotNetRenderer _renderer;

    public PlaywrightDotNetBackend()
        : this(new PlaywrightDotNetRenderer())
    {
    }

    public PlaywrightDotNetBackend(PlaywrightDotNetRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public TargetSpec Target { get; } = new("playwright-dotnet", "csharp", "playwright");

    public IReadOnlyCollection<string> Aliases { get; } = new[]
    {
        "dotnet",
        "csharp",
        "cs",
        "pw-dotnet",
        "playwright-csharp"
    };

    public string Render(TestFileModel model) => _renderer.Render(model);

    public string RenderDocument(MigrationDocument document) => this.RenderDocumentViaLegacyBridge(document);

    public string GetDefaultFileName(TestFileModel model) => $"{model.ClassName}Playwright.cs";
}
