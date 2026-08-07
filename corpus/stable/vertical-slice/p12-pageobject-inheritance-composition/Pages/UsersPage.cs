using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P12.Pages;
public sealed class UsersPage : BasePage
{
    public UsersPage(IWebDriver driver) : base(driver) => Modal = new ModalComponent(driver);
    public ModalComponent Modal { get; }
}
