using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P33;

// Adversarial wrapper: mimics the WebDriver/ISearchContext surface (FindElement /
// FindElements taking real Selenium By / returning real IWebElement) while NOT being
// the actual driver. The source project compiles and runs because it delegates to the
// real IWebDriver. The migrator must NOT mistake calls on this wrapper for Selenium
// idioms (no bogus .Locator(...) mapping, no false PASS) - the wrapper usage must
// degrade into honest UNRESOLVED_SYMBOL/TODO diagnostics instead.
public sealed class ImposterDriver
{
    readonly IWebDriver _inner;

    public ImposterDriver(IWebDriver inner)
    {
        _inner = inner;
    }

    public IWebElement FindElement(By by) => _inner.FindElement(by);

    public IReadOnlyList<IWebElement> FindElements(By by) => _inner.FindElements(by);
}
