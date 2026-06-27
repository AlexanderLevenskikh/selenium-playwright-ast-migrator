using System.Text;
using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Built-in target backend for Playwright Test TypeScript output.
/// </summary>
public sealed class PlaywrightTypeScriptBackend : ITargetBackend
{
    readonly PlaywrightTypeScriptRenderer _renderer;

    public PlaywrightTypeScriptBackend()
        : this(new PlaywrightTypeScriptRenderer())
    {
    }

    public PlaywrightTypeScriptBackend(PlaywrightTypeScriptRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public TargetSpec Target { get; } = new("playwright-typescript", "typescript", "playwright");

    public IReadOnlyCollection<string> Aliases { get; } = new[]
    {
        "ts",
        "typescript",
        "pw-ts",
        "playwright-ts"
    };

    public string Render(TestFileModel model) => _renderer.Render(model);

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
