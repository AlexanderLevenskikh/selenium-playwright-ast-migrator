using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class PythonWaitsAssertionsMatrixTests
{
    [Fact]
    public void PythonParser_WaitsMatrix_ProducesStableWaitIntents()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_python_wait_matrix.py");
            File.WriteAllText(file, """
            from selenium.webdriver.common.by import By
            from selenium.webdriver.support.ui import WebDriverWait
            from selenium.webdriver.support import expected_conditions as EC
            from selenium.webdriver.support.expected_conditions import visibility_of_element_located

            def test_wait_matrix(driver):
                status = driver.find_element(By.CSS_SELECTOR, ".status")
                wait = WebDriverWait(driver, 10)
                wait.until(EC.visibility_of_element_located((By.ID, "save")))
                wait.until(visibility_of_element_located((By.CSS_SELECTOR, ".row")))
                wait.until(EC.invisibility_of_element_located((By.CSS_SELECTOR, ".loader")))
                wait.until(EC.presence_of_all_elements_located((By.CSS_SELECTOR, ".result")))
                wait.until(EC.text_to_be_present_in_element((By.ID, "status"), "Saved"))
                wait.until(EC.visibility_of(status))
                wait.until_not(EC.visibility_of(status))
                WebDriverWait(driver, 5).until(EC.element_to_be_clickable(driver.find_element(By.NAME, "query")))
                WebDriverWait(driver, 5).until(EC.invisibility_of(driver.find_element_by_css_selector(".toast")))
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "status" && l.LocatorExpression == ".status");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == ".row");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden && w.Target.RenderLocator() == ".loader");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateLoaded && w.Target.RenderLocator() == ".result");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateLoaded && w.Target.RenderLocator() == "#status" && w.SourceMethod == "EC.text_to_be_present_in_element");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == ".status");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden && w.Target.RenderLocator() == ".status");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible && w.Target.RenderLocator() == "[name='query']");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden && w.Target.RenderLocator() == ".toast");

            var document = LegacyIrBridge.ToDocument(model, source: new SourceSpec("selenium-python", "python", "selenium"));
            var waitIntents = document.Suite.Tests.Single().Body
                .OfType<WaitStatementIr>()
                .Select(s => Assert.IsType<LocatorWaitIntent>(s.Intent))
                .ToArray();

            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateVisible) && intent.SourceMethod == "EC.visibility_of_element_located");
            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateHidden) && intent.SourceMethod == "EC.invisibility_of_element_located");
            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateLoaded) && intent.SourceMethod == "EC.text_to_be_present_in_element");
            Assert.Contains(waitIntents, intent => intent.Kind == nameof(WaitForKind.ProductStateVisible) && intent.SourceMethod == "EC.element_to_be_clickable");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void PythonParser_AssertionsMatrix_ProducesTextAndVisibilityIntents()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_python_assertion_matrix.py");
            File.WriteAllText(file, """
            import unittest
            from selenium.webdriver.common.by import By

            class TestAssertions(unittest.TestCase):
                def test_assertion_matrix(self):
                    status = self.driver.find_element(By.CSS_SELECTOR, ".status")
                    assert status.text == "Ready"
                    assert status.text != "Error"
                    assert "Ready" in status.text
                    assert "Error" not in status.text
                    assert status.is_displayed()
                    assert not self.driver.find_element(By.ID, "error").is_displayed()
                    assert self.driver.find_element(By.ID, "status").text == "Saved"
                    assert "Saved" in self.driver.find_element(By.CSS_SELECTOR, ".status").text
                    self.assertEqual("Saved", status.text)
                    self.assertEqual(status.text, "Saved")
                    self.assertIn("Saved", status.text)
                    self.assertNotIn("Error", status.text)
                    self.assertTrue(status.is_displayed())
                    self.assertFalse(self.driver.find_element(By.ID, "error").is_displayed())
                    self.assertEqual("Saved", self.driver.find_element(By.ID, "status").text)
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Ready\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextNotEquals && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Error\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Ready\"");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Visible && v.Target.RenderLocator() == ".status");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Hidden && v.Target.RenderLocator() == "#error");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals && t.Target.RenderLocator() == "#status" && t.ExpectedValue == "\"Saved\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains && t.Target.RenderLocator() == ".status" && t.ExpectedValue == "\"Saved\"");

            var document = LegacyIrBridge.ToDocument(model, source: new SourceSpec("selenium-python", "python", "selenium"));
            var assertions = document.Suite.Tests.Single().Body
                .OfType<AssertionStatementIr>()
                .Select(s => s.Intent)
                .ToArray();

            Assert.Contains(assertions, intent => intent is TextAssertionIntent text && text.Kind == nameof(TextAssertionKind.TextEquals));
            Assert.Contains(assertions, intent => intent is TextAssertionIntent text && text.Kind == nameof(TextAssertionKind.TextContains));
            Assert.Contains(assertions, intent => intent is TextAssertionIntent text && text.Kind == nameof(TextAssertionKind.TextNotEquals));
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
        var path = Path.Combine(Path.GetTempPath(), "python-waits-assertions-matrix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
