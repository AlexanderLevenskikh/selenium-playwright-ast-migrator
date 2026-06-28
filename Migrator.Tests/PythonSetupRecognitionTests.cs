using Migrator.Core.Models;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class PythonSetupRecognitionTests
{
    [Fact]
    public void PythonParser_SetupMethodSelfLocators_AreAvailableInsideTests()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_setup_method.py");
            File.WriteAllText(file, """
            from selenium import webdriver
            from selenium.webdriver.common.by import By
            from selenium.webdriver.common.keys import Keys
            from selenium.webdriver.support.ui import WebDriverWait
            from selenium.webdriver.support import expected_conditions as EC

            class TestLogin:
                def setup_method(self, method):
                    self.driver = webdriver.Chrome()
                    self.driver.get("/login")
                    self.save_button = self.driver.find_element(By.ID, "save")
                    self.wait = WebDriverWait(self.driver, 10)

                def test_save(self):
                    self.save_button.click()
                    self.save_button.send_keys(Keys.ENTER)
                    self.wait.until(EC.visibility_of(self.save_button))
                    assert self.save_button.is_displayed()
                    assert self.save_button.text == "Saved"
                    assert "Sav" in self.save_button.text
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);
            var setupActions = model.SetUpActions.ToArray();
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(setupActions, a => a is UnsupportedAction);
            Assert.Contains(setupActions, a => a is NavigationAction);
            Assert.Contains(setupActions, a => a is LocatorDeclarationAction l && l.VariableName == "save_button" && l.LocatorExpression == "#save");
            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is ClickAction c && c.Target.RenderLocator() == "#save");
            Assert.Contains(actions, a => a is PressAction p && p.Target.RenderLocator() == "#save" && p.KeyName == "Enter");
            Assert.Contains(actions, a => a is WaitForAction w && w.Target.RenderLocator() == "#save" && w.Kind == WaitForKind.ProductStateVisible);
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Target.RenderLocator() == "#save" && v.Kind == VisibilityKind.Visible);
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Target.RenderLocator() == "#save" && t.Kind == TextAssertionKind.TextEquals && t.ExpectedValue == "\"Saved\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Target.RenderLocator() == "#save" && t.Kind == TextAssertionKind.TextContains && t.ExpectedValue == "\"Sav\"");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void PythonParser_SetupClassClsLocators_CanBackSelfReferencesInTests()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_setup_class.py");
            File.WriteAllText(file, """
            from selenium.webdriver.common.by import By

            class TestStatus:
                @classmethod
                def setup_class(cls):
                    cls.driver.get("/cart")
                    cls.status = cls.driver.find_element(By.CSS_SELECTOR, ".status")

                def test_status(self):
                    assert self.status.text == "Ready"
                    assert self.status.is_displayed()
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);
            var setupActions = model.SetUpActions.ToArray();
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(setupActions, a => a is UnsupportedAction);
            Assert.Contains(setupActions, a => a is NavigationAction);
            Assert.Contains(setupActions, a => a is LocatorDeclarationAction l && l.VariableName == "status" && l.LocatorExpression == ".status");
            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Target.RenderLocator() == ".status" && t.Kind == TextAssertionKind.TextEquals);
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Target.RenderLocator() == ".status" && v.Kind == VisibilityKind.Visible);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void PythonParser_UnittestSetUp_SuppressesDriverBoilerplateAndSeedsLocators()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_unittest_setup.py");
            File.WriteAllText(file, """
            import unittest
            from selenium import webdriver
            from selenium.webdriver.common.by import By

            class TestProfile(unittest.TestCase):
                def setUp(self):
                    super().setUp()
                    self.driver = webdriver.Chrome()
                    self.name = self.driver.find_element(By.NAME, "name")

                def test_fills_name(self):
                    self.name.clear()
                    self.name.send_keys("Alex")
                    assert not self.name.is_displayed()
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);
            var setupActions = model.SetUpActions.ToArray();
            var actions = Assert.Single(model.Tests).BodyActions.ToArray();

            Assert.DoesNotContain(setupActions, a => a is UnsupportedAction);
            Assert.Single(setupActions.OfType<LocatorDeclarationAction>());
            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is SendKeysAction s && s.Target.RenderLocator() == "[name='name']" && s.TextExpression == "\"\"");
            Assert.Contains(actions, a => a is SendKeysAction s && s.Target.RenderLocator() == "[name='name']" && s.TextExpression == "\"Alex\"");
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Target.RenderLocator() == "[name='name']" && v.Kind == VisibilityKind.Hidden);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "python-setup-recognition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
