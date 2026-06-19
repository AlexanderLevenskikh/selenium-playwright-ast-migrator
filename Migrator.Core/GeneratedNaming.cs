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

    public static string ApplyClassNameSuffix(string className, string? classNameSuffix)
    {
        if (string.IsNullOrWhiteSpace(classNameSuffix))
            return className;

        return className.EndsWith(classNameSuffix, StringComparison.Ordinal)
            ? className
            : className + classNameSuffix;
    }

    public static string GetPlaywrightFileName(string sourceClassName)
    {
        return ApplyPlaywrightSuffixOnce(sourceClassName) + ".cs";
    }
}
