namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Stable identity and aliases for the Playwright TypeScript target backend.
/// Keep config keys, diagnostics, and CLI aliases centralized so TS is treated
/// as a first-class target rather than a string literal scattered through renderers.
/// </summary>
public static class PlaywrightTypeScriptTarget
{
    public const string Id = "playwright-typescript";
    public const string Language = "typescript";
    public const string Framework = "playwright";

    public static readonly IReadOnlyCollection<string> Aliases = new[]
    {
        "ts",
        "typescript",
        "pw-ts",
        "playwright-ts"
    };
}
