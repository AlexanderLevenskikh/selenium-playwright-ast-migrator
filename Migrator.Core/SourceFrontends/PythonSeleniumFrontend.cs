using Migrator.Core;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Experimental source frontend for Python Selenium pytest/unittest-style tests.
/// </summary>
public sealed class PythonSeleniumFrontend : TestFileParserSourceFrontend
{
    public static readonly SourceSpec Spec = new("selenium-python", "python", "selenium");

    public PythonSeleniumFrontend()
        : base(Spec, new PythonSeleniumTestFileParser(), AliasesList)
    {
    }

    static readonly string[] AliasesList =
    {
        "python-selenium",
        "selenium-python",
        "py-selenium",
        "pytest-selenium",
        "selenium-py",
        "python",
        "py"
    };
}
