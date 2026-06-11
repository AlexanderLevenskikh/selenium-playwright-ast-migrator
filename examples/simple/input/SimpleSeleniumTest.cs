using MyProject.E2ETests.PageObjects;
using NUnit.Framework;
using OpenQA.Selenium;

namespace MyProject.E2ETests.Tests;

public class SimpleSeleniumTest : TestBase
{
    private LoginPage page;

    [SetUp]
    public void SetUp()
    {
        page = Navigation.OpenLoginPage();
        page.Loader.ValidateLoading();
    }

    [Test]
    public void LoginWithValidCredentials()
    {
        page.Username.InputText("admin");
        page.Password.InputText("secret123");
        page.SubmitButton.Click();
    }

    [Test]
    public void SearchInDashboard()
    {
        page.SearchBox.InputText("report");
        page.SearchBox.InputText(Keys.Enter);
        page.SearchButton.Click();
    }
}
