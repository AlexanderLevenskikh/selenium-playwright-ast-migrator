using System.Text;

namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Emits the TypeScript-side test host: imports, optional describe wrapper,
/// and test declarations. Keeping this separate lets renderer parity tests
/// protect generated test bodies while host/import policy evolves independently.
/// </summary>
public sealed class PlaywrightTypeScriptTestHostRenderer
{
    static readonly string[] DefaultImports =
    {
        "import { test, expect } from '@playwright/test';"
    };

    readonly PlaywrightTypeScriptRenderOptions _options;

    public PlaywrightTypeScriptTestHostRenderer(PlaywrightTypeScriptRenderOptions? options = null)
    {
        _options = options ?? PlaywrightTypeScriptRenderOptions.Default;
    }

    public void RenderPreamble(StringBuilder sb, string sourcePath, string targetDescription)
    {
        foreach (var import in GetImportLines())
            sb.AppendLine(import);

        sb.AppendLine();
        sb.AppendLine($"// Generated from {_options.SourceLabel}: {EscapeComment(sourcePath)}");
        sb.AppendLine($"// Target: {targetDescription}");
        sb.AppendLine();
    }

    public int BeginSuite(StringBuilder sb, string suiteName)
    {
        if (!_options.UseDescribe)
            return 0;

        var name = string.IsNullOrWhiteSpace(_options.DescribeName) ? suiteName : _options.DescribeName!;
        sb.AppendLine($"{TestFunctionName}.describe('{EscapeString(name)}', () => {{");
        return 1;
    }

    public void EndSuite(StringBuilder sb)
    {
        if (_options.UseDescribe)
        {
            sb.AppendLine("});");
            sb.AppendLine();
        }
    }

    public int BeginTest(StringBuilder sb, string testName, int suiteIndent)
    {
        var pad = new string(' ', suiteIndent * 2);
        sb.AppendLine($"{pad}{TestFunctionName}('{EscapeString(testName)}', async ({FixtureParameter}) => {{");
        return suiteIndent + 1;
    }

    public void EndTest(StringBuilder sb, int suiteIndent)
    {
        var pad = new string(' ', suiteIndent * 2);
        sb.AppendLine($"{pad}}});");
        sb.AppendLine();
    }

    public IReadOnlyList<string> GetImportLines()
    {
        var source = _options.ImportLines.Count == 0 ? DefaultImports : _options.ImportLines;
        return source
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
    }

    string TestFunctionName => string.IsNullOrWhiteSpace(_options.TestFunctionName) ? "test" : _options.TestFunctionName.Trim();
    string FixtureParameter => string.IsNullOrWhiteSpace(_options.FixtureParameter) ? "{ page }" : _options.FixtureParameter.Trim();

    static string EscapeString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    static string EscapeComment(string value) => value.Replace("*/", "* /", StringComparison.Ordinal);
}
