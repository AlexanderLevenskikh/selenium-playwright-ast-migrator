using Migrator.Core;
namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Experimental source frontend for Java Selenium tests.
/// </summary>
public sealed class JavaSeleniumFrontend : TestFileParserSourceFrontend
{
    public static readonly SourceSpec Spec = new("selenium-java", "java", "selenium");

    public JavaSeleniumFrontend()
        : base(Spec, new JavaSeleniumTestFileParser(), AliasesList)
    {
    }

    static readonly string[] AliasesList =
    {
        "java-selenium",
        "selenium-java",
        "junit-selenium"
    };
}
