using FluentAssertions;
using NUnit.Framework;

namespace TestRecognizers.E2ETests;

public class NewPatternsFixture : TestBase
{
    private SomePage page;

    [SetUp]
    public void SetUp()
    {
        var pagef = Navigation.GoToAsync("/registry");
        page = pagef;
    }

    [Test]
    public void CheckClickAsync()
    {
        page.SubmitButton.ClickAsync();
    }

    [Test]
    public void CheckFillAsync()
    {
        page.SearchField.FillAsync("test value");
    }

    [Test]
    public void CheckPressAsync()
    {
        page.InputField.PressAsync("Enter");
    }

    [Test]
    public void CheckSelectValue()
    {
        page.StatusDropdown.SelectValue("active");
    }

    [Test]
    public void CheckPlaywrightAssertion()
    {
        Expect(page.ResultText).ToHaveTextAsync("expected text");
        Expect(page.HiddenElement).ToBeHiddenAsync();
    }

    [Test]
    public void CheckLocalDeclaration()
    {
        var code = page.GetItemCode();
        var name = page.GetDisplayName();
        var irrelevant = CalculateSomething(42);
        Assert.That(code, Is.Not.Empty);
    }
}
