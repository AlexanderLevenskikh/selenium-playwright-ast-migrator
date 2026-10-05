using System;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;

public class T
{
    IWebDriver WebDriver;

    [Test]
    public void PerformsActionsChain()
    {
        var target = WebDriver.FindElement(By.Id("m"));
        new Actions(WebDriver).MoveToElement(target).Click().Perform();
    }
}
