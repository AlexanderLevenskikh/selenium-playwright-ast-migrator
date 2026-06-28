using Migrator.Core.Models;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class PythonSeleniumSpikeTests
{
    [Fact]
    public void PythonParser_PytestAndUnittestSetup_RecognizesSetupAndTests()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_login.py");
            File.WriteAllText(file, """
            from selenium.webdriver.common.by import By

            class TestLogin:
                def setup_method(self):
                    self.driver.get("/login")

                def test_saves_name(self):
                    save = self.driver.find_element(By.ID, "save")
                    save.click()

            def test_plain_function(driver):
                driver.find_element(By.CSS_SELECTOR, ".name").send_keys("Alex")
            """);

            var model = new PythonSeleniumTestFileParser().Parse(file);

            Assert.Equal("TestLogin", model.ClassName);
            Assert.IsType<NavigationAction>(Assert.Single(model.SetUpActions));
            Assert.Equal(2, model.Tests.Count());
            Assert.Contains(model.Tests, t => t.Name == "test_saves_name");
            Assert.Contains(model.Tests, t => t.Name == "test_plain_function");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void PythonParser_CommonSeleniumPatterns_ProduceCanonicalLegacyActions()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_catalog.py");
            File.WriteAllText(file, """
            from selenium.webdriver.common.by import By
            from selenium.webdriver.support.ui import WebDriverWait
            from selenium.webdriver.support import expected_conditions as EC
            from selenium.webdriver.common.keys import Keys

            def test_filters_catalog(driver):
                driver.get("/catalog")
                filter_input = driver.find_element(By.CSS_SELECTOR, ".filter")
                filter_input.clear()
                filter_input.send_keys("Milk")
                filter_input.send_keys(Keys.ENTER)
                WebDriverWait(driver, 10).until(EC.visibility_of_element_located((By.CSS_SELECTOR, ".filter")))
                WebDriverWait(driver, 10).until(EC.invisibility_of_element_located((By.ID, "loader")))
                assert filter_input.is_displayed()
                assert filter_input.text == "Milk"
                assert "Ready" in driver.find_element(By.CSS_SELECTOR, ".status").text
                assert not driver.find_element(By.ID, "error").is_displayed()
            """);

            var actions = Assert.Single(new PythonSeleniumTestFileParser().Parse(file).Tests).BodyActions.ToArray();

            Assert.Contains(actions, a => a is NavigationAction);
            Assert.Contains(actions, a => a is LocatorDeclarationAction l && l.VariableName == "filter_input");
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
    public void PythonParser_UnrecognizedStatements_ArePreservedAsUnsupportedTodo()
    {
        var temp = CreateTempDir();
        try
        {
            var file = Path.Combine(temp, "test_unsupported.py");
            File.WriteAllText(file, """
            def test_project_helper(driver):
                custom_login_as_admin(driver)
            """);

            var action = Assert.Single(Assert.Single(new PythonSeleniumTestFileParser().Parse(file).Tests).BodyActions);
            var unsupported = Assert.IsType<UnsupportedAction>(action);
            Assert.Equal("PYTHON_SELENIUM_SPIKE_UNRECOGNIZED_STATEMENT", unsupported.Reason);
            Assert.Equal("custom_login_as_admin(driver)", unsupported.SourceText);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "python-selenium-spike-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
