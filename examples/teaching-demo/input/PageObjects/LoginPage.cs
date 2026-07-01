using OpenQA.Selenium;

namespace MyProduct.E2ETests.PageObjects;

public sealed class LoginPage
{
    private readonly IWebDriver driver;

    public LoginPage(IWebDriver driver)
    {
        this.driver = driver;
    }

    // These project wrappers are intentionally small. Real projects often hide
    // selectors behind TextInput/Button/Control classes; Migrator should recover
    // source truth from the POM or from adapter-config, not invent selectors.
    public TextInput UserName => new(driver, By.CssSelector("[data-testid='login-email']"));
    public TextInput Password => new(driver, By.CssSelector("[data-testid='login-password']"));
    public Button SignInButton => new(driver, By.CssSelector("[data-testid='sign-in']"));
    public Control DashboardTitle => new(driver, By.CssSelector("[data-testid='dashboard-title']"));
    public Control PasswordError => new(driver, By.CssSelector("[data-testid='password-error']"));

    public void WaitUntilReady()
    {
        Loader.ValidateLoading();
    }
}
