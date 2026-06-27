using Migrator.Core;
using Migrator.Core.SourceFrontends;

namespace Migrator.Roslyn;

/// <summary>
/// Built-in source frontend for Selenium tests written in C#.
/// This keeps Roslyn-specific parsing behind the source-frontend contract.
/// </summary>
public sealed class CSharpSeleniumFrontend : TestFileParserSourceFrontend
{
    public static readonly SourceSpec Spec = new("selenium-csharp", "csharp", "selenium");

    public CSharpSeleniumFrontend()
        : base(Spec, new RoslynTestFileParser(), AliasesList)
    {
    }

    public CSharpSeleniumFrontend(ProjectAdapterConfig? config)
        : base(Spec, new RoslynTestFileParser(config), AliasesList)
    {
    }

    static readonly string[] AliasesList =
    {
        "csharp-selenium",
        "selenium-cs",
        "selenium-dotnet",
        "cs-selenium"
    };
}
