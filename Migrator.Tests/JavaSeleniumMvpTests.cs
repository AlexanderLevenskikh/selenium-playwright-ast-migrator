using Migrator.Core.Models;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class JavaSeleniumMvpTests
{
    [Fact]
    public void JavaParser_JUnitAndTestNgAnnotations_RecognizesSetupAndTests()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "LoginTests.java");
            File.WriteAllText(file, """
            package sample.tests;

            import org.junit.jupiter.api.BeforeEach;
            import org.junit.jupiter.api.Test;
            import org.testng.annotations.BeforeMethod;
            import org.openqa.selenium.By;
            import org.openqa.selenium.WebDriver;

            public class LoginTests {
                private WebDriver driver;

                @BeforeEach
                public void openApp() {
                    driver.get("/login");
                }

                @Test
                public void savesName() {
                    WebElement save = driver.findElement(By.id("save"));
                    save.click();
                }

                @org.testng.annotations.Test
                public void testNgSmoke() {
                    driver.findElement(By.cssSelector(".name")).sendKeys("Alex");
                }
            }
            """);

            var model = new JavaSeleniumTestFileParser().Parse(file);

            Assert.Equal("sample.tests", model.Namespace);
            Assert.Equal("LoginTests", model.ClassName);
            Assert.IsType<NavigationAction>(Assert.Single(model.SetUpActions));
            Assert.Equal(2, model.Tests.Count());
            Assert.Contains(model.Tests, t => t.Name == "savesName");
            Assert.Contains(model.Tests, t => t.Name == "testNgSmoke");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void JavaParser_CommonWebDriverPatterns_ProduceCanonicalLegacyActions()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "CatalogTests.java");
            File.WriteAllText(file, """
            package sample.tests;

            import org.junit.jupiter.api.Test;
            import static org.junit.jupiter.api.Assertions.*;
            import org.openqa.selenium.*;
            import org.openqa.selenium.support.ui.*;

            public class CatalogTests {
                private WebDriver driver;
                private WebDriverWait wait;

                @Test
                public void filtersCatalog() {
                    driver.navigate().to("/catalog");
                    WebElement filter = driver.findElement(By.cssSelector(".filter"));
                    filter.clear();
                    filter.sendKeys("Milk");
                    filter.sendKeys(Keys.ENTER);
                    wait.until(ExpectedConditions.visibilityOf(filter));
                    new WebDriverWait(driver, Duration.ofSeconds(10)).until(ExpectedConditions.invisibilityOfElementLocated(By.id("loader")));
                    assertTrue(filter.isDisplayed());
                    assertEquals("Milk", filter.getText());
                    assertTrue(driver.findElement(By.cssSelector(".status")).getText().contains("Ready"));
                    assertFalse(driver.findElement(By.id("error")).isDisplayed());
                }
            }
            """);

            var test = Assert.Single(new JavaSeleniumTestFileParser().Parse(file).Tests);
            var actions = test.BodyActions.ToArray();

            Assert.Contains(actions, a => a is NavigationAction);
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "filter");
            Assert.Contains(actions, a => a is SendKeysAction s && s.TextExpression == "\"\"");
            Assert.Contains(actions, a => a is SendKeysAction s && s.TextExpression == "\"Milk\"");
            Assert.Contains(actions, a => a is PressAction p && p.KeyName == "Enter");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible);
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden);
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Visible);
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Hidden);
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals);
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void JavaParser_UnrecognizedStatements_ArePreservedAsUnsupportedTodo()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "UnsupportedTests.java");
            File.WriteAllText(file, """
            import org.junit.Test;

            public class UnsupportedTests {
                @Test
                public void usesProjectHelper() {
                    customLoginAsAdmin();
                }
            }
            """);

            var action = Assert.Single(Assert.Single(new JavaSeleniumTestFileParser().Parse(file).Tests).BodyActions);
            var unsupported = Assert.IsType<UnsupportedAction>(action);
            Assert.Equal("JAVA_SELENIUM_MVP_UNRECOGNIZED_STATEMENT", unsupported.Reason);
            Assert.Equal("customLoginAsAdmin()", unsupported.SourceText);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "java-selenium-mvp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
