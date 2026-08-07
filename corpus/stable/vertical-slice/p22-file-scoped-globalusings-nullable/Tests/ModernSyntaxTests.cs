namespace Migrator.Lab.Corpus.P22;

public partial class ModernSyntaxTests
{
    [Test]
    public void FileScopedNamespaceGlobalUsingsAndNullableCompile()
    {
        IWebElement? button = WebDriver.FindElement(By.Id("smoke-button"));
        Assert.That(button, Is.Not.Null);
        button!.Click();
        Assert.That(WebDriver.FindElement(By.Id("smoke-status")).Text, Is.EqualTo("ok"));
    }
}
