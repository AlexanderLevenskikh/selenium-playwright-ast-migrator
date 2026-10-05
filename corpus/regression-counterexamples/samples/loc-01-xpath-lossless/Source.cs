using System;
using NUnit.Framework;
using OpenQA.Selenium;

public class T
{
    IWebDriver WebDriver;

    [Test]
    public void FindsByXpath()
    {
        var el = WebDriver.FindElement(By.XPath("//div[@id='main']"));
        el.Click();
    }
}
