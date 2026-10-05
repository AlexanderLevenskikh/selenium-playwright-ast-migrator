using System;
using NUnit.Framework;
using OpenQA.Selenium;

public class T
{
    IWebDriver WebDriver;

    [Test]
    public void AssertsMultipleFluent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebDriver.Title, Is.EqualTo("ok"));
            Assert.That(WebDriver.Url, Does.Contain("example"));
        });
    }
}
