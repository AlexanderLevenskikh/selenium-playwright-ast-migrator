using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P12.Pages;
public abstract class BasePage
{
    protected BasePage(IWebDriver driver) => Driver = driver;
    protected IWebDriver Driver { get; }
}
