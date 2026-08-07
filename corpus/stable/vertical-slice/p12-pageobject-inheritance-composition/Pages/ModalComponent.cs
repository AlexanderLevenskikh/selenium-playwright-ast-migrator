using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P12.Pages;
public sealed class ModalComponent
{
    readonly IWebDriver driver;
    public ModalComponent(IWebDriver driver) => this.driver = driver;
    public void OpenAndSave()
    {
        driver.FindElement(By.Id("modal-open")).Click();
        driver.FindElement(By.Id("modal-save")).Click();
    }
}
