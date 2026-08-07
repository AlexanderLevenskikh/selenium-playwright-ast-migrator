using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace Example.Block6;

public class PipelineBlock6PrimitivePatternsTests
{
    [Test]
    public void ClearValueAndControlStates()
    {
        var input = WebDriver.FindElement(By.CssSelector(".name"));
        input.Clear();
        input.SendKeys("new");
        Assert.That(input.GetAttribute("value"), Is.EqualTo("new"));

        var terms = WebDriver.FindElement(By.Id("terms"));
        var optional = WebDriver.FindElement(By.Id("optional"));
        var blocked = WebDriver.FindElement(By.Id("blocked"));
        Assert.That(terms.Selected, Is.True);
        Assert.That(optional.Selected, Is.False);
        Assert.That(terms.Enabled, Is.True);
        Assert.That(blocked.Enabled, Is.False);
    }

    [Test]
    public void LocalAndConditionalByAliases()
    {
        var target = By.Id("locator-primary");
        WebDriver.FindElement(target).Click();

        const bool usePrimary = true;
        var conditionalTarget = usePrimary
            ? By.Id("locator-primary")
            : By.Id("locator-secondary");
        WebDriver.FindElement(conditionalTarget).Click();
    }

    [Test]
    public void NegativeWaitAndNullableLocator()
    {
        var wait = new WebDriverWait(WebDriver, TimeSpan.FromSeconds(3));
        wait.Until(driver => !driver.FindElement(By.Id("negative-spinner")).Displayed);

        IWebElement? button = WebDriver.FindElement(By.Id("negative-save"));
        Assert.That(button, Is.Not.Null);
        Assert.That(button.Enabled, Is.True);
        button!.Click();
    }
}
