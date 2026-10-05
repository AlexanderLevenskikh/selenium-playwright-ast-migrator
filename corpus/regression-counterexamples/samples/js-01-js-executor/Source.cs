using System;
using NUnit.Framework;
using OpenQA.Selenium;

public class T
{
    IWebDriver WebDriver;

    [Test]
    public void ScrollsWithJavascript()
    {
        ((IJavaScriptExecutor)WebDriver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
    }
}
