using Example.E2ETests.Infrastructure;
using Example.E2ETests.Infrastructure.PageElements;
using Example.E2ETests.PageObjects.Widget;
using FluentAssertions;
using NUnit.Framework;
using OpenQA.Selenium;

namespace Example.E2ETests.Tests.Functional;

public class Widget : TestBase
{
    private WidgetPage page;

    [SetUp]
    public void SetUp()
    {
        var pagef = Navigation.OpenSearchPage();
        pagef.Loader.ValidateLoading();
        var lightbox = pagef.WidgetButton.ClickAndOpen<WidgetPage>();
        page = lightbox;
        page.User.Visible.Wait().EqualTo(true);
        page.FuterUser.Visible.Wait().EqualTo(true);
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckUserToWidget()
    {
        page.User.Click();
        page.UserInput.InputTextAndSelectValue("Test User");
        page.FuterUser.Visible.Wait().EqualTo(true);
        page.FuterUser.Text.Get().Should().NotBeEmpty();
    }

    [Test]
    public void CheckDateToWidget()
    {
        page.WidgetDate.ManualInputValue("March", "2025", 22);
        page.Loader.ValidateLoading();
        page.FuterUser.Visible.Wait().EqualTo(true);
        page.FuterUser.Text.Get().Should().NotBeEmpty();
    }

    [Test]
    public void CheckSearchToWidget()
    {
        page.WidgetSearch.InputText("Example invoice 2024");
        page.WidgetSearch.InputText(Keys.Enter);
        page.Loader.ValidateLoading();
        page.FuterUser.WaitPresence();
        page.FuterUser.Text.Get().Should().NotBeEmpty();
    }
}
