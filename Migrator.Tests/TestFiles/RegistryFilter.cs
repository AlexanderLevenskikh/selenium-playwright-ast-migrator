using System.Linq;
using Example.E2ETests.Infrastructure;
using Example.E2ETests.PageObjects;
using FluentAssertions;
using NUnit.Framework;

namespace Example.E2ETests.Tests.Filters;

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
        page.Sc.InputAndSelect("1000000001");
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(2).Text.Get().Should().Contain("0004");
    }

    [TestCase("Ascending", "0005")]
    [TestCase("Descending", "0346")]
    [Category("QuickRunning")]
    public void CheckFilterScSortAndExcludeToRegistry(string sortOrder, string text)
    {
        page.Sc.ExcludeValue("1000000001");
        page.Loader.ValidateLoading();
        page.Sc.SortSc(sortOrder);
        page.Loader.ValidateLoading();
        page.Table.Items.ElementAt(2).Text.Get().Should().Contain(text);
        page.Sc.ClearSort();
        page.Loader.ValidateLoading();
    }

    [TestCase("Ascending", 0)]
    [TestCase("Descending", 1234567.89)]
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
        page.ReportsSubtotalSalesAmount.Sum.Get().Should().Be(1000000.00m);
        page.ReportsSubtotalAddCalcAmount.Sum.Get().Should().Be(-1000.00m);
        page.ReportsSubtotalReward.Sum.Get().Should().Be(500000.00m);
    }
}
