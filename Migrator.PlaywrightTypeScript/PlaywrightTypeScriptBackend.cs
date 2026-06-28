using System.Text;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;

namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Built-in target backend for Playwright Test TypeScript output.
/// </summary>
public sealed class PlaywrightTypeScriptBackend : ITargetBackend
{
    readonly PlaywrightTypeScriptRenderer _renderer;
    readonly PlaywrightTypeScriptIrV2Renderer _irV2Renderer;

    public PlaywrightTypeScriptBackend()
        : this(PlaywrightTypeScriptRenderOptions.Default)
    {
    }

    public PlaywrightTypeScriptBackend(PlaywrightTypeScriptRenderOptions options)
        : this(new PlaywrightTypeScriptRenderer(options), new PlaywrightTypeScriptIrV2Renderer(options))
    {
    }

    public PlaywrightTypeScriptBackend(PlaywrightTypeScriptRenderer renderer)
        : this(renderer, new PlaywrightTypeScriptIrV2Renderer())
    {
    }

    public PlaywrightTypeScriptBackend(PlaywrightTypeScriptRenderer renderer, PlaywrightTypeScriptIrV2Renderer irV2Renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _irV2Renderer = irV2Renderer ?? throw new ArgumentNullException(nameof(irV2Renderer));
    }

    public TargetSpec Target { get; } = new(
        PlaywrightTypeScriptTarget.Id,
        PlaywrightTypeScriptTarget.Language,
        PlaywrightTypeScriptTarget.Framework);

    public IReadOnlyCollection<string> Aliases { get; } = PlaywrightTypeScriptTarget.Aliases;

    public string Render(TestFileModel model) => _renderer.Render(model);

    public string RenderDocument(MigrationDocument document) => _irV2Renderer.Render(document);

    public string GetDefaultFileName(TestFileModel model) => $"{ToKebabCase(model.ClassName)}.spec.ts";

    static string ToKebabCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "generated-test";

        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('-');

            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }
}
