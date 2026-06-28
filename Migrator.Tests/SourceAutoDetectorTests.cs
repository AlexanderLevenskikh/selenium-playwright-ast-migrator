using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class SourceAutoDetectorTests
{
    [Fact]
    public void Detect_JavaSelenium_ReturnsHighConfidenceJava()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "LoginTests.java"), """
            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.By;
            import org.openqa.selenium.WebDriver;

            public class LoginTests {
                private WebDriver driver;
                @Test public void saves() {
                    driver.findElement(By.id("save")).click();
                }
            }
            """);

            var report = SourceAutoDetector.Detect(temp);

            Assert.Equal("selenium-java", report.DetectedSourceId);
            Assert.Equal("high", report.Confidence);
            Assert.Contains(report.Reasons, r => r.Contains("org.openqa.selenium", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(report.Candidates, c => c.SourceId == "selenium-java" && c.Score > 0);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void Detect_PythonSelenium_ReturnsHighConfidencePython()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "test_login.py"), """
            from selenium.webdriver.common.by import By

            def test_login(driver):
                driver.find_element(By.ID, "save").click()
            """);

            var report = SourceAutoDetector.Detect(temp);

            Assert.Equal("selenium-python", report.DetectedSourceId);
            Assert.Equal("high", report.Confidence);
            Assert.Contains(report.Reasons, r => r.Contains("selenium.webdriver", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void Detect_CSharpSelenium_ReturnsHighConfidenceCSharp()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "LoginTests.cs"), """
            using NUnit.Framework;
            using OpenQA.Selenium;

            public class LoginTests {
                private IWebDriver Driver;
                [Test] public void Saves() {
                    Driver.FindElement(By.Id("save")).Click();
                }
            }
            """);

            var report = SourceAutoDetector.Detect(temp);

            Assert.Equal("selenium-csharp", report.DetectedSourceId);
            Assert.Equal("high", report.Confidence);
            Assert.Contains(report.Reasons, r => r.Contains("OpenQA.Selenium", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void Detect_NoSignals_FallsBackToCSharpWithNoneConfidence()
    {
        var temp = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(temp, "README.txt"), "not test source");

            var report = SourceAutoDetector.Detect(temp);

            Assert.Equal("selenium-csharp", report.DetectedSourceId);
            Assert.Equal("none", report.Confidence);
            Assert.Contains("falling back", string.Join("\n", report.Reasons), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "migrator-source-detect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
