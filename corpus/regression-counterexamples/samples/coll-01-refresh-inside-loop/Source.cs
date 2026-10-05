using System;
using NUnit.Framework;
using OpenQA.Selenium;

public class T
{
    IWebDriver WebDriver;

    [Test]
    public void RefreshesCollectionInsideLoop()
    {
        var rows = WebDriver.FindElements(By.CssSelector("tr.row"));
        foreach (var row in rows)
        {
            var id = row.GetAttribute("data-id");
            rows = WebDriver.FindElements(By.CssSelector("tr.row"));
            if (id == "3")
                row.Click();
        }
    }
}
