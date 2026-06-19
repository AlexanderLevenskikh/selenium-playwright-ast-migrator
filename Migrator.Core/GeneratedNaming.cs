namespace Migrator.Core;

/// <summary>
/// Helpers for stable generated Playwright names.
/// </summary>
public static class GeneratedNaming
{
    public const string PlaywrightSuffix = "Playwright";

    public static string ApplyPlaywrightSuffixOnce(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        return name.EndsWith(PlaywrightSuffix, StringComparison.Ordinal)
            ? name
            : name + PlaywrightSuffix;
    }

    public static string GetPlaywrightFileName(string sourceClassName)
    {
        return ApplyPlaywrightSuffixOnce(sourceClassName) + ".cs";
    }
}
