using NUnit.Framework;

namespace Migrator.PublicDemo.Selenium.NUnit;

public class LoginSmokeTest
{
    private DemoShopApp app = null!;

    [SetUp]
    public void SetUp()
    {
        app = Navigation.OpenDemoShop();
        app.Loader.ValidateLoading();
    }

    [Test]
    [Category("Smoke")]
    public void LoginFilterCartAndCheckout()
    {
        app.Login.Username.InputText("admin@example.test");
        app.Login.Password.InputText("correct-horse-battery-staple");
        app.Login.SubmitButton.Click();
        app.Login.SuccessToast.ShouldBeVisible();

        app.Catalog.Search.InputText("mug");
        app.Catalog.ResultCount.ShouldHaveText("1 item");
        app.Catalog.AddMugButton.Click();
        app.Cart.Count.ShouldHaveText("1");
        app.Cart.OpenButton.Click();
        app.Cart.Total.ShouldHaveText("$12.00");
        app.Cart.CheckoutButton.Click();
        app.Orders.Status.ShouldContainText("Order demo-1001 created");
    }
}

public static class Navigation
{
    public static DemoShopApp OpenDemoShop() => new();
}

public sealed class DemoShopApp
{
    public LoginPage Login { get; } = new();
    public CatalogPage Catalog { get; } = new();
    public CartPage Cart { get; } = new();
    public OrdersPage Orders { get; } = new();
    public DemoLoader Loader { get; } = new();
}

public sealed class LoginPage
{
    public DemoControl Username { get; } = new("login-username");
    public DemoControl Password { get; } = new("login-password");
    public DemoControl SubmitButton { get; } = new("login-submit");
    public DemoControl SuccessToast { get; } = new("login-success-toast");
}

public sealed class CatalogPage
{
    public DemoControl Search { get; } = new("catalog-search");
    public DemoControl ResultCount { get; } = new("catalog-result-count");
    public DemoControl AddMugButton { get; } = new("catalog-add-mug");
}

public sealed class CartPage
{
    public DemoControl Count { get; } = new("cart-count");
    public DemoControl OpenButton { get; } = new("cart-open");
    public DemoControl Total { get; } = new("cart-total");
    public DemoControl CheckoutButton { get; } = new("checkout");
}

public sealed class OrdersPage
{
    public DemoControl Status { get; } = new("orders-status");
}

public sealed class DemoControl
{
    public DemoControl(string testId) => TestId = testId;
    public string TestId { get; }
    public void InputText(string value) { }
    public void Click() { }
    public void ShouldBeVisible() { }
    public void ShouldHaveText(string value) { }
    public void ShouldContainText(string value) { }
}

public sealed class DemoLoader
{
    public void ValidateLoading() { }
}
