using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class JavaWaitsAssertionsMatrixTests
{
    [Fact]
    public void JavaParser_WaitsMatrix_ProducesStableWaitIntents()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "WaitMatrixTests.java");
            File.WriteAllText(file, """
            package sample.waits;

            import java.time.Duration;
            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.*;
            import org.openqa.selenium.support.ui.*;
            import static org.openqa.selenium.support.ui.ExpectedConditions.*;

            public class WaitMatrixTests {
                private WebDriver driver;
                private WebDriverWait wait;

                @Test
                public void waitsAcrossCommonForms() {
                    WebDriverWait localWait = new WebDriverWait(driver, Duration.ofSeconds(10));
                    WebElement save = driver.findElement(By.id("save"));
                    wait.until(ExpectedConditions.visibilityOfElementLocated(By.id("save")));
                    wait.until(visibilityOfElementLocated(By.cssSelector(".row")));
                    wait.until(ExpectedConditions.invisibilityOfElementLocated(By.cssSelector(".loader")));
                    localWait.until(ExpectedConditions.invisibilityOf(driver.findElement(By.cssSelector(".toast"))));
                    wait.until(ExpectedConditions.elementToBeClickable(save));
                    new WebDriverWait(driver, Duration.ofSeconds(5)).until(elementToBeClickable(By.name("query")));
                }
            }
            """);

            var model = new JavaSeleniumTestFileParser().Parse(file);
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(actions, a => a is UnsupportedAction u && u.SourceText.Contains("WebDriverWait localWait", StringComparison.Ordinal));
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "save" && l.LocatorExpression == "#save");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == ".row");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden && w.Target.RenderLocator() == ".loader");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden && w.Target.RenderLocator() == ".toast");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == "[name='query']");

            var document = LegacyIrBridge.ToDocument(model, source: new SourceSpec("selenium-java", "java", "selenium"));
            var waitIntents = document.Suite.Tests.Single().Body
                .OfType<WaitStatementIr>()
                .Select(s => Assert.IsType<LocatorWaitIntent>(s.Intent))
                .ToArray();

            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateVisible) && intent.SourceMethod == "ExpectedConditions.visibilityOfElementLocated");
            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateHidden) && intent.SourceMethod == "ExpectedConditions.invisibilityOf");
            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateVisible) && intent.SourceMethod == "ExpectedConditions.elementToBeClickable");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void JavaParser_AssertionsMatrix_ProducesTextAndVisibilityIntents()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "AssertionMatrixTests.java");
            File.WriteAllText(file, """
            package sample.assertions;

            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.*;
            import static org.junit.jupiter.api.Assertions.*;
            import static org.hamcrest.MatcherAssert.assertThat;
            import static org.hamcrest.Matchers.*;

            public class AssertionMatrixTests {
                private WebDriver driver;

                @Test
                public void assertionsAcrossCommonForms() {
                    WebElement status = driver.findElement(By.cssSelector(".status"));
                    Assertions.assertEquals("status text", "Ready", driver.findElement(By.cssSelector(".status")).getText());
                    Assert.assertEquals(driver.findElement(By.id("save-result")).getText(), "Saved");
                    assertTrue("save visible", driver.findElement(By.id("save")).isDisplayed());
                    assertFalse(driver.findElement(By.id("error")).isDisplayed());
                    assertThat(driver.findElement(By.cssSelector(".status")).getText(), containsString("Ready"));
                    assertThat(status.getText(), equalTo("Saved"));
                    assertThat(driver.findElement(By.id("save")).isDisplayed(), is(true));
                    assertThat(status.isDisplayed(), is(false));
                    assertThat(driver.findElement(By.id("error")).isDisplayed()).isFalse();
                    assertThat(status.getText()).contains("Saved");
                }
            }
            """);

            var model = new JavaSeleniumTestFileParser().Parse(file);
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Ready\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals && t.Target.RenderLocator() == "#save-result" && t.ExpectedValue == "\"Saved\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Ready\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Saved\"");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Visible && v.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Hidden && v.Target.RenderLocator() == "#error");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Hidden && v.Target.RenderLocator() == ".status");

            var document = LegacyIrBridge.ToDocument(model, source: new SourceSpec("selenium-java", "java", "selenium"));
            var assertions = document.Suite.Tests.Single().Body
                .OfType<AssertionStatementIr>()
                .Select(s => s.Intent)
                .ToArray();

            Assert.Contains(assertions, intent => intent is TextAssertionIntent text && text.Kind == nameof(TextAssertionKind.TextEquals));
            Assert.Contains(assertions, intent => intent is TextAssertionIntent text && text.Kind == nameof(TextAssertionKind.TextContains));
            Assert.Contains(assertions, intent => intent is VisibilityAssertionIntent visibility && visibility.Kind == nameof(VisibilityKind.Visible));
            Assert.Contains(assertions, intent => intent is VisibilityAssertionIntent visibility && visibility.Kind == nameof(VisibilityKind.Hidden));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "java-waits-assertions-matrix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
