using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace Migrator.Lab.Corpus.P06;

public abstract class LabSeleniumTestBase
{
    protected IWebDriver WebDriver { get; private set; } = null!;
    protected abstract string RelativePath { get; }

    [SetUp]
    public void StartBrowser()
    {
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--window-size=1280,900");

        var binary = Environment.GetEnvironmentVariable("MIGRATOR_LAB_CHROME_BINARY");
        if (!string.IsNullOrWhiteSpace(binary))
            options.BinaryLocation = binary;

        var driverDirectory = Environment.GetEnvironmentVariable("MIGRATOR_LAB_CHROMEDRIVER_DIRECTORY");
        WebDriver = string.IsNullOrWhiteSpace(driverDirectory)
            ? new ChromeDriver(options)
            : new ChromeDriver(ChromeDriverService.CreateDefaultService(driverDirectory), options);
        WebDriver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        WebDriver.Navigate().GoToUrl(BuildPageUri(RelativePath).AbsoluteUri);
    }

    [TearDown]
    public void StopBrowser()
    {
        WebDriver?.Quit();
        WebDriver?.Dispose();
    }

    static Uri BuildPageUri(string relativePath)
    {
        var baseUrl = Environment.GetEnvironmentVariable("MIGRATOR_LAB_APP_URL") ?? "http://127.0.0.1:5057/";
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            baseUrl += "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath.TrimStart('/'));
    }
}

public partial class FormStateTests : LabSeleniumTestBase
{
    protected override string RelativePath => "/form";
}
