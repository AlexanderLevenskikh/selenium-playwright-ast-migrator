namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Target-side host options for Playwright TypeScript output.
/// This keeps TS imports/fixtures/test wrappers target-owned instead of scattering
/// string literals across source frontends or migration adapters.
/// </summary>
public sealed class PlaywrightTypeScriptRenderOptions
{
    public static PlaywrightTypeScriptRenderOptions Default { get; } = new();

    /// <summary>
    /// Complete import lines to place at the top of the generated spec file.
    /// When empty, the renderer emits the default Playwright Test import.
    /// Duplicate lines are removed while preserving order.
    /// </summary>
    public IReadOnlyList<string> ImportLines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Test declaration function. Defaults to Playwright Test's "test".
    /// Project fixtures can set this to an imported alias such as "authTest".
    /// </summary>
    public string TestFunctionName { get; init; } = "test";

    /// <summary>
    /// Fixture parameter text used in generated test callbacks.
    /// Defaults to "{ page }" and may be changed to project fixtures like
    /// "{ page, loginPage }".
    /// </summary>
    public string FixtureParameter { get; init; } = "{ page }";

    /// <summary>
    /// Whether to wrap generated tests in test.describe(...). Disabled by default
    /// to preserve legacy output and golden snapshots.
    /// </summary>
    public bool UseDescribe { get; init; }

    /// <summary>
    /// Optional describe name. When absent and UseDescribe is true, the suite class
    /// name is used.
    /// </summary>
    public string? DescribeName { get; init; }

    /// <summary>
    /// Label used in the generated source comment. Defaults to the historic text
    /// so old snapshots remain stable.
    /// </summary>
    public string SourceLabel { get; init; } = "Selenium C# source";
}
