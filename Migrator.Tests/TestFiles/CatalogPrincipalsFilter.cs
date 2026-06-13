using System.Linq;
using Example.E2ETests.PageObjects;
using FluentAssertions;
using NUnit.Framework;

namespace Example.E2ETests.Tests.Filters;

public class CatalogPrincipalsFilter : TestBase
{
    private CatalogPrincipalsPage page;

    [SetUp]
    public void SetUp()
    {
        var pagef = Navigation.OpenCatalogPrincipalPage();
        pagef.Loader.ValidateLoading();
        page = pagef;
    }


    [Test]
    [Category("QuickRunning")]
    public void CheckFilterInnToPrincipals()
    {
        page.Inn.InputInputAndAccept("6673240328");
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(2).Text.Get().Should().Be("6673240328");
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckFilterKppToPrincipals()
    {
        page.Kpp.InputInputAndAccept("668601001");
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(3).Text.Get().Should().Be("668601001");
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckFilterNameToPrincipals()
    {
        page.Name.SelectValue("Контур");
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(0).Text.Get().Should().Contain("Производственная Фирма");
        page.Table.Items.ElementAt(1).Text.Get().Should().Be("Контур");
    }

    [TestCase("По возрастанию", "Контур")]
    [TestCase("По убыванию", "НТТ")]
    [Category("QuickRunning")]
    public void CheckFilterNameSortToPrincipals(string sortOrder, string text)
    {
        page.NameSort.Sort(sortOrder);
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(1).Text.Get().Should().Be(text);
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckActivityToPrincipals()
    {
        page.Active.Click();
        page.Loader.ValidateLoadingPartner();
        page.Count.Text.Get().Should().Be("5");
    }
}