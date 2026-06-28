using Migrator.Core.Models;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class PythonSeleniumFixturesPackTests
{
    [Fact]
    public void PythonParser_PytestClassAndUnittestCaseFixtures_AreRecognized()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_fixture_shapes.py");
            File.WriteAllText(file, """
            import unittest
            from selenium.webdriver.common.by import By

            class TestCart:
                def setup_method(self):
                    self.driver.get("/cart")

                def test_pytest_class_style(self):
                    self.save_button = self.driver.find_element(By.ID, "save")
                    self.save_button.click()
                    assert self.save_button.is_displayed()

            class TestLogin(unittest.TestCase):
                def setUp(self):
                    self.driver.get("/login")

                def test_unittest_style(self):
                    login = self.driver.find_element(By.NAME, "login")
                    login.clear()
                    login.send_keys("alex")
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);
            var tests = model.Tests.ToArray();
            var actions = tests.SelectMany(t => t.BodyActions).ToArray();

            Assert.Equal("TestCart", model.ClassName);
            Assert.Equal(2, model.SetUpActions.Count());
            Assert.Equal(2, tests.Length);
            Assert.Contains(tests, t => t.Name == "test_pytest_class_style");
            Assert.Contains(tests, t => t.Name == "test_unittest_style");
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "save_button");
            Assert.Contains(actions, a => a is ClickAction);
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Visible);
            Assert.Contains(actions, a => a is SendKeysAction s && s.TextExpression == "\"\"");
            Assert.Contains(actions, a => a is SendKeysAction s && s.TextExpression == "\"alex\"");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void PythonParser_WebDriverWaitVariablesAndExpectedConditions_AreRecognized()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_waits.py");
            File.WriteAllText(file, """
            from selenium.webdriver.common.by import By
            from selenium.webdriver.support.ui import WebDriverWait
            from selenium.webdriver.support import expected_conditions as EC
            from selenium.webdriver.support import expected_conditions
            from selenium.webdriver.support.expected_conditions import invisibility_of_element_located

            def test_wait_matrix(driver):
                status = driver.find_element(By.CSS_SELECTOR, ".status")
                wait = WebDriverWait(driver, 10)
                wait.until(EC.visibility_of(status))
                wait.until(expected_conditions.element_to_be_clickable((By.ID, "save")))
                WebDriverWait(driver, 5).until(invisibility_of_element_located((By.CSS_SELECTOR, ".loader")))
            """);

            var actions = Assert.Single(new PythonSeleniumTestFileParser().Parse(file).Tests).BodyActions.ToArray();

            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "status");
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateVisible);
            Assert.Contains(actions, a => a is WaitForAction w && w.Kind == WaitForKind.ProductStateHidden);
            Assert.Equal(3, actions.OfType<WaitForAction>().Count());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void PythonParser_LegacyFindElementByHelpers_AreRecognized()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_legacy_selenium.py");
            File.WriteAllText(file, """
            def test_legacy_helpers(driver):
                driver.find_element_by_id("save").click()
                name = driver.find_element_by_name("name")
                name.clear()
                name.send_keys("Alex")
                assert name.text == "Alex"
                assert "Saved" in driver.find_element_by_css_selector(".status").text
                assert not driver.find_element_by_xpath("//div[@id='error']").is_displayed()
            """);

            var actions = Assert.Single(new PythonSeleniumTestFileParser().Parse(file).Tests).BodyActions.ToArray();

            Assert.DoesNotContain(actions, a => a is UnsupportedAction);
            Assert.Contains(actions, a => a is ClickAction);
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "name");
            Assert.Contains(actions, a => a is SendKeysAction s && s.TextExpression == "\"\"");
            Assert.Contains(actions, a => a is SendKeysAction s && s.TextExpression == "\"Alex\"");
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextEquals);
            Assert.Contains(actions, a => a is TextAssertionAction t && t.Kind == TextAssertionKind.TextContains);
            Assert.Contains(actions, a => a is VisibilityAssertionAction v && v.Kind == VisibilityKind.Hidden);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "python-selenium-fixtures-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
