using MyProduct.E2ETests.PageObjects;
using NUnit.Framework;

namespace MyProduct.E2ETests.Tests;

public class LoginTeachingTest : TestBase
{
    private LoginPage page = null!;

    [SetUp]
    public void SetUp()
    {
        page = Navigation.OpenLoginPage();
        page.WaitUntilReady();
    }

    [Test]
    public void ValidUserCanSignIn()
    {
        page.UserName.InputText("alex@example.com");
        page.Password.InputText("correct horse battery staple");
        page.SignInButton.Click();

        page.DashboardTitle.ShouldBeVisible();
        Assert.That(page.DashboardTitle.Text, Does.Contain("Dashboard"));
    }

    [Test]
    public void EmptyPasswordShowsValidation()
    {
        page.UserName.InputText("alex@example.com");
        page.SignInButton.Click();

        Assert.That(page.PasswordError.Text, Does.Contain("Password is required"));
    }
}
