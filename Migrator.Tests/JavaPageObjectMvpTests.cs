using Migrator.Core.Models;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class JavaPageObjectMvpTests
{
    [Fact]
    public void JavaParser_PageObjectByFields_InlineSimplePomMethodFromDirectory()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "LoginPage.java"), """
            package sample.pages;

            import org.openqa.selenium.*;

            public class LoginPage {
                private WebDriver driver;
                private final By saveButton = By.id("save");
                private By nameInput = By.name("name");

                public void enterNameAlex() {
                    driver.findElement(nameInput).clear();
                    driver.findElement(nameInput).sendKeys("Alex");
                }

                public void save() {
                    driver.findElement(saveButton).click();
                }
            }
            """);

            File.WriteAllText(Path.Combine(temp, "LoginTests.java"), """
            package sample.tests;

            import org.junit.jupiter.api.Test;

            public class LoginTests {
                @Test
                public void savesThroughPageObject() {
                    LoginPage loginPage = new LoginPage(driver);
                    loginPage.enterNameAlex();
                    loginPage.save();
                }
            }
            """);

            var model = Assert.Single(new JavaSeleniumTestFileParser().ParseDirectory(temp).Where(m => m.ClassName == "LoginTests"));
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(actions, a => a is UnsupportedAction u && u.SourceText.Contains("loginPage", StringComparison.Ordinal));
            Assert.Contains(actions, a => a is SendKeysAction s && s.Target.RenderLocator() == "[name='name']" && s.TextExpression == "\"\"");
            Assert.Contains(actions, a => a is SendKeysAction s && s.Target.RenderLocator() == "[name='name']" && s.TextExpression == "\"Alex\"");
            Assert.Contains(actions, a => a is ClickAction c && c.Target.RenderLocator() == "#save");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void JavaParser_PageObjectFieldsAssignedInSetup_AreAvailableInTests()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "CatalogPage.java"), """
            import org.openqa.selenium.*;

            public class CatalogPage {
                private final By filter = By.cssSelector(".filter");

                public void clearFilter() {
                    driver.findElement(filter).clear();
                }
            }
            """);

            File.WriteAllText(Path.Combine(temp, "CatalogTests.java"), """
            import org.junit.jupiter.api.BeforeEach;
            import org.junit.jupiter.api.Test;

            public class CatalogTests {
                private CatalogPage catalogPage;

                @BeforeEach
                public void setUp() {
                    catalogPage = new CatalogPage(driver);
                }

                @Test
                public void clearsFilter() {
                    catalogPage.clearFilter();
                }
            }
            """);

            var model = Assert.Single(new JavaSeleniumTestFileParser().ParseDirectory(temp).Where(m => m.ClassName == "CatalogTests"));
            var action = Assert.Single(Assert.Single(model.Tests).BodyActions);
            var clear = Assert.IsType<SendKeysAction>(action);
            Assert.Equal(".filter", clear.Target.RenderLocator());
            Assert.Equal("\"\"", clear.TextExpression);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void JavaParser_FindByFields_AreRecognizedAndPageFactoryIsLimitedDiagnostic()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "SavePage.java"), """
            import org.openqa.selenium.*;
            import org.openqa.selenium.support.FindBy;
            import org.openqa.selenium.support.PageFactory;

            public class SavePage {
                @FindBy(id = "save")
                private WebElement saveButton;

                @FindBy(css = ".status")
                private WebElement status;

                public SavePage(WebDriver driver) {
                    PageFactory.initElements(driver, this);
                }

                public void save() {
                    saveButton.click();
                }

                public void assertSaved() {
                    assertTrue(this.status.getText().contains("Saved"));
                }
            }
            """);

            File.WriteAllText(Path.Combine(temp, "SaveTests.java"), """
            import org.junit.jupiter.api.Test;

            public class SaveTests {
                @Test
                public void savesWithFindByPageObject() {
                    SavePage savePage = new SavePage(driver);
                    savePage.save();
                    savePage.assertSaved();
                }
            }
            """);

            var model = Assert.Single(new JavaSeleniumTestFileParser().ParseDirectory(temp).Where(m => m.ClassName == "SaveTests"));
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.Contains(actions, a => a is UnsupportedAction u && u.Reason == "JAVA_PAGEFACTORY_LIMITED_EXPERIMENTAL");
            Assert.Contains(actions, a => a is ClickAction c && c.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "java-page-object-mvp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
