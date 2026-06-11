using System.Linq;
using ArBilling.E2ETests.Infrastructure;
using ArBilling.E2ETests.PageObjects;
using FluentAssertions;
using NUnit.Framework;

namespace ArBilling.E2ETests.Tests.Filters;

public class RegistryFilter : TestBase
{
    private RegistryAgentPage page;

    [SetUp]
    public void SetUp()
    {
        var pagef = Navigation.OpenRegistryAgentPage();
        pagef.Loader.ValidateLoading();
        page = pagef;
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckFilterScToRegistry()
    {
        page.Sc.InputAndSelect("8610013950");
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(2).Text.Get().Should().Contain("0004");
    }

    [TestCase("По возрастанию", "0005")]
    [TestCase("По убыванию", "0346")]
    [Category("QuickRunning")]
    public void CheckFilterScSortAndExcludeToRegistry(string sortOrder, string text)
    {
        page.Sc.ExcludeValue("8610013950");
        page.Loader.ValidateLoading();
        page.Sc.SortSc(sortOrder);
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(2).Text.Get().Should().Contain(text);
        page.Sc.ClearSort();
        page.Loader.ValidateLoading();
    }

    [TestCase("По возрастанию", 0)]
    [TestCase("По убыванию", 1452041.54)]
    [Category("QuickRunning")]
    public void CheckFilterRealizationToRegistry(string sortOrder, decimal text)
    {
        page.SalesAmount.Sort(sortOrder);
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(4).Sum.Get().Should().Be(text);
    }

    [Test]
    [Category("QuickRunning")]
    public void CheckSubtotalToRegistry()
    {
        page.ReportsSubtotalSalesAmount.Sum.Get().Should().Be(2988323.95m);
        page.ReportsSubtotalAddCalcAmount.Sum.Get().Should().Be(-5409.00m);
        page.ReportsSubtotalReward.Sum.Get().Should().Be(484811.84m);
    }
}
