using Migrator.Core.Models;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class JavaSeleniumFixturesPackTests
{
    [Fact]
    public void FixturePack_JUnit4JUnit5AndTestNg_FindsSetupAndTestMethods()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "JUnit4LoginTests.java"), """
            package sample.junit4;

            import org.junit.Before;
            import org.junit.Test;
            import org.openqa.selenium.WebDriver;

            public class JUnit4LoginTests {
                private WebDriver driver;

                @Before
                public void openLogin() {
                    driver.get("/login");
                }

                @Test
                public void savesUser() {
                    driver.findElement(By.id("save")).click();
                }
            }
            """);

            File.WriteAllText(Path.Combine(temp, "JUnit5CatalogTests.java"), """
            package sample.junit5;

            import org.junit.jupiter.api.BeforeEach;
            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.WebDriver;

            public class JUnit5CatalogTests {
                private WebDriver driver;

                @BeforeEach
                void openCatalog() {
                    driver.navigate().to("/catalog");
                }

                @Test
                void filtersCatalog() {
                    driver.findElement(By.cssSelector(".filter")).sendKeys("Milk");
                }
            }
            """);

            File.WriteAllText(Path.Combine(temp, "TestNgSmokeTests.java"), """
            package sample.testng;

            import org.testng.annotations.BeforeMethod;
            import org.testng.annotations.Test;
            import org.openqa.selenium.WebDriver;

            public class TestNgSmokeTests {
                private WebDriver driver;

                @BeforeMethod
                public void openPage() {
                    driver.get("/smoke");
                }

                @Test
                public void smoke() {
                    driver.findElement(By.name("query")).clear();
                }
            }
            """);

            var models = new JavaSeleniumTestFileParser().ParseDirectory(temp).OrderBy(m => m.ClassName).ToArray();

            Assert.Equal(3, models.Length);
            Assert.All(models, model => Assert.Single(model.SetUpActions));
            Assert.All(models, model => Assert.Single(model.Tests));
            Assert.Contains(models, model => model.ClassName == "JUnit4LoginTests" && AssertSingleAction<ClickAction>(model));
            Assert.Contains(models, model => model.ClassName == "JUnit5CatalogTests" && AssertSingleAction<SendKeysAction>(model));
            Assert.Contains(models, model => model.ClassName == "TestNgSmokeTests" && AssertSingleAction<SendKeysAction>(model));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void FixturePack_ByFieldsAndLocalByVariables_ResolveThroughFindElementCalls()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "ByVariableTests.java");
            File.WriteAllText(file, """
            package sample.byvars;

            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.*;
            import org.openqa.selenium.support.ui.*;

            public class ByVariableTests {
                private final By saveButton = By.id("save");
                private By statusMessage = By.cssSelector(".status");

                @Test
                public void usesByVariables() {
                    By nameInput = By.name("name");
                    driver.findElement(saveButton).click();
                    driver.findElement(nameInput).clear();
                    driver.findElement(nameInput).sendKeys("Alex");
                    wait.until(ExpectedConditions.visibilityOfElementLocated(statusMessage));
                    assertEquals("Saved", driver.findElement(statusMessage).getText());
                    assertTrue(driver.findElement(saveButton).isDisplayed());
                    WebElement toast = driver.findElement(statusMessage);
                    assertTrue(toast.getText().contains("Saved"));
                }
            }
            """);

            var actions = Assert.Single(new JavaSeleniumTestFileParser().Parse(file).Tests).BodyActions.ToArray();

            Assert.Contains(actions, a => a is ClickAction c && c.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is SendKeysAction s && s.Target.RenderLocator() == "[name='name']" && s.TextExpression == "\"\"");
            Assert.Contains(actions, a => a is SendKeysAction s && s.Target.RenderLocator() == "[name='name']" && s.TextExpression == "\"Alex\"");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == ".status");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals && t.Target.RenderLocator() == ".status");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Visible && v.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "toast" && l.LocatorExpression == ".status");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void FixturePack_WaitsAndAssertionsMatrix_RecognizesCommonExpectedConditions()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "WaitAssertionMatrixTests.java");
            File.WriteAllText(file, """
            package sample.waits;

            import org.testng.annotations.Test;
            import org.openqa.selenium.*;
            import org.openqa.selenium.support.ui.*;

            public class WaitAssertionMatrixTests {
                @Test
                public void waitsAndAssertions() {
                    wait.until(ExpectedConditions.visibilityOfElementLocated(By.id("save")));
                    wait.until(ExpectedConditions.presenceOfElementLocated(By.cssSelector(".row")));
                    wait.until(ExpectedConditions.elementToBeClickable(By.name("query")));
                    wait.until(ExpectedConditions.invisibilityOfElementLocated(By.cssSelector(".loader")));
                    assertTrue(driver.findElement(By.id("save")).isDisplayed());
                    assertFalse(driver.findElement(By.id("error")).isDisplayed());
                    assertEquals("Ready", driver.findElement(By.cssSelector(".status")).getText());
                    assertTrue(driver.findElement(By.cssSelector(".status")).getText().contains("Ready"));
                }
            }
            """);

            var actions = Assert.Single(new JavaSeleniumTestFileParser().Parse(file).Tests).BodyActions.ToArray();

            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateLoaded && w.Target.RenderLocator() == ".row");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == "[name='query']");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden && w.Target.RenderLocator() == ".loader");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Visible && v.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Hidden && v.Target.RenderLocator() == "#error");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals && t.Target.RenderLocator() == ".status");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void FixturePack_UnsupportedProjectHelpers_RemainActionableTodos()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "UnsupportedHelperTests.java");
            File.WriteAllText(file, """
            import org.junit.Test;

            public class UnsupportedHelperTests {
                @Test
                public void usesProjectSpecificHelpers() {
                    customLoginAsAdmin();
                    catalogPage.openAdvancedFilter();
                }
            }
            """);

            var actions = Assert.Single(new JavaSeleniumTestFileParser().Parse(file).Tests).BodyActions.ToArray();

            Assert.Equal(2, actions.Length);
            Assert.All(actions, action =>
            {
                var unsupported = Assert.IsType<UnsupportedAction>(action);
                Assert.Equal("JAVA_SELENIUM_MVP_UNRECOGNIZED_STATEMENT", unsupported.Reason);
            });
            Assert.Contains(actions, a => ((UnsupportedAction)a).SourceText == "customLoginAsAdmin()");
            Assert.Contains(actions, a => ((UnsupportedAction)a).SourceText == "catalogPage.openAdvancedFilter()");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static bool AssertSingleAction<TAction>(TestFileModel model) where TAction : TestAction
    {
        var test = Assert.Single(model.Tests);
        Assert.Contains(test.BodyActions, action => action is TAction);
        return true;
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "java-selenium-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
