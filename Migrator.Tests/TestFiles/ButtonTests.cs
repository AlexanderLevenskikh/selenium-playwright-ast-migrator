using ArBilling.E2ETests.Infrastructure;
using ArBilling.E2ETests.PageObjects;
using FluentAssertions;
using NUnit.Framework;

namespace ArBilling.E2ETests.Tests.NonCategory;

public class ButtonTests : TestBase
{
    private RegistryAgentPage page;

    [SetUp]
    public void SetUp()
    {
        var pagef = Navigation.OpenRegistryAgentPage();
        page = pagef;
        page.Loader.ValidateLoading();
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckSearchButton()
    {
        page.MenuItems.SideMenuButtonSearch.Click();
        page.MenuItems.SearchTextArea.Visible.Wait().EqualTo(true);
        page.MenuItems.SearchTextArea.Visible.Wait().EqualTo(false);
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckFeedBackButton()
    {
        page.MenuItems.SideMenuButtonFeedback.Visible.Wait().EqualTo(true);
        page.MenuItems.SideMenuButtonFeedback.Text.Get().Should().Be("Оставить отзыв");
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckButtonCatalogsPartners()
    {
        page.MenuItems.SideMenuCatalogs.Click();
        page.MenuItems.SideMenuCatalogsPartners.Click();
        WebDriver.Url.Should().Be(Urls.BaseUrlCatalogPartners);
        page.MenuItems.Error.Visible.Wait().EqualTo(false);
    }
}
