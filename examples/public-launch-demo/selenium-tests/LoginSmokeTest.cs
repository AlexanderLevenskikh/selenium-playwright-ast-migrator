using NUnit.Framework;

namespace PublicLaunchDemo.SeleniumTests;

public class LoginSmokeTest : TestBase
{
    private LoginPage page = null!;

    [SetUp]
    public void SetUp()
    {
        page = Navigation.OpenLoginPage();
        page.Loader.ValidateLoading();
    }

    [Test]
    public void LoginWithValidCredentials()
    {
        page.Username.InputText("admin@example.test");
        page.Password.InputText("correct-horse-battery-staple");
        page.SubmitButton.Click();
        page.Toast.ShouldBeVisible();
    }
}
