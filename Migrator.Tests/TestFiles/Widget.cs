using ArBilling.E2ETests.Infrastructure;
using ArBilling.E2ETests.Infrastructure.PageElements;
using ArBilling.E2ETests.PageObjects.Widget;
using FluentAssertions;
using NUnit.Framework;
using OpenQA.Selenium;

namespace ArBilling.E2ETests.Tests.Functional;

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
        page.UserInput.InputTextAndSelectValue("Selenium-администатор");
        page.FuterUser.Visible.Wait().EqualTo(true);
        page.FuterUser.Text.Get().Should().NotBeEmpty();
    }

    [Test]
    public void CheckDateToWidget()
    {
        page.WidgetDate.ManualInputValue("Март", "2025", 22);
        page.Loader.ValidateLoading();
        page.FuterUser.Visible.Wait().EqualTo(true);
        page.FuterUser.Text.Get().Should().NotBeEmpty();
    }

    [Test]
    public void CheckSearchToWidget()
    {
        page.WidgetSearch.InputText("Отправка в Диадок АНО ДПО 12.2022 (СЦ 0669)");
        page.WidgetSearch.InputText(Keys.Enter);
        page.Loader.ValidateLoading();
        page.FuterUser.WaitPresence();
        page.FuterUser.Text.Get().Should().NotBeEmpty();
    }
}
