using NUnit.Framework;

namespace Migrator.PublicDemo.Selenium.NUnit;

public class LoginSmokeTest
{
    private LoginPage page = null!;

    [SetUp]
    public void SetUp()
    {
        page = Navigation.OpenLoginPage();
        page.Loader.ValidateLoading();
    }

    [Test]
    [Category("Smoke")]
    public void LoginWithValidCredentials()
    {
        page.Username.InputText("admin@example.test");
        page.Password.InputText("correct-horse-battery-staple");
        page.SubmitButton.Click();
        page.Toast.ShouldBeVisible();
    }
}

public static class Navigation
{
    public static LoginPage OpenLoginPage() => new();
}

public sealed class LoginPage
{
    public DemoControl Username { get; } = new("login-username");
    public DemoControl Password { get; } = new("login-password");
    public DemoControl SubmitButton { get; } = new("login-submit");
    public DemoControl Toast { get; } = new("login-success-toast");
    public DemoLoader Loader { get; } = new();
}

public sealed class DemoControl
{
    public DemoControl(string testId) => TestId = testId;
    public string TestId { get; }
    public void InputText(string value) { }
    public void Click() { }
    public void ShouldBeVisible() { }
}

public sealed class DemoLoader
{
    public void ValidateLoading() { }
}
