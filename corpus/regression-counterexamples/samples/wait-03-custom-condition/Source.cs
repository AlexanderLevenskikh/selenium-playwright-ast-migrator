using System;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

public class T
{
    IWebDriver WebDriver;

    [Test]
    public void WaitsForCustomCondition()
    {
        var el = WebDriver.FindElement(By.Id("b"));
        el.Click();
        WaitUntil(() => el.Displayed);
    }

    static void WaitUntil(Func<bool> condition)
    {
        var wait = new WebDriverWait(Environment.CurrentDirectory, TimeSpan.FromSeconds(3));
        wait.Until(_ => condition());
    }
}
