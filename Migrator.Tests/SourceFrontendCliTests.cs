using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
public class SourceFrontendCliTests
{
    [Fact]
    public void Migrate_SourceJavaSelenium_TargetTypeScript_GeneratesSpecFile()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "java-input");
            var outputDir = Path.Combine(temp, "generated");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "LoginTests.java"), """
            package sample.tests;

            import org.junit.jupiter.api.Test;
            import static org.junit.jupiter.api.Assertions.assertEquals;
            import org.openqa.selenium.By;
            import org.openqa.selenium.WebDriver;

            public class LoginTests {
                private WebDriver driver;

                @Test
                public void savesName() {
                    driver.findElement(By.id("save")).click();
                    driver.findElement(By.cssSelector(".name")).sendKeys("Alex");
                    assertEquals("Saved", driver.findElement(By.cssSelector(".status")).getText());
                }
            }
            """);

            var result = RunCli($"--mode migrate --source java-selenium --input \"{inputDir}\" --out \"{outputDir}\" --target ts --format both");

            AssertCliSuccess(result);
            var generatedFiles = Directory.GetFiles(outputDir, "*.spec.ts");
            Assert.Single(generatedFiles);

            var generated = File.ReadAllText(generatedFiles[0]);
            Assert.Contains("test('savesName'", generated);
            Assert.Contains("page.locator('#save')", generated);
            Assert.Contains("page.locator('.name')", generated);
            Assert.Contains("toHaveText", generated);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void DumpIr_SourceJavaSelenium_V2_RecordsJavaSourceSpec()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "java-input");
            var outputDir = Path.Combine(temp, "ir-dump");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "SmokeTests.java"), """
            package sample.tests;

            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.By;
            import org.openqa.selenium.WebDriver;

            public class SmokeTests {
                private WebDriver driver;

                @Test
                public void clicksSave() {
                    driver.findElement(By.id("save")).click();
                }
            }
            """);

            var result = RunCli($"--mode dump-ir --source java-selenium --input \"{inputDir}\" --out \"{outputDir}\" --target ts --ir-version v2 --format json");

            AssertCliSuccess(result);
            var dumpPath = Path.Combine(outputDir, "ir-dump.json");
            Assert.True(File.Exists(dumpPath), "Expected ir-dump.json to be written.");

            using var json = JsonDocument.Parse(File.ReadAllText(dumpPath));
            var root = json.RootElement;
            Assert.Equal("test-ir/v2", root.GetProperty("SchemaVersion").GetString());
            Assert.Equal("selenium-java", root.GetProperty("Source").GetProperty("Id").GetString());
            Assert.Equal("java", root.GetProperty("Source").GetProperty("Language").GetString());
            Assert.Equal("playwright-typescript", root.GetProperty("Target").GetProperty("Id").GetString());
        }
        finally
        {
            TryDelete(temp);
        }
    }


    [Fact]
    public void Migrate_WithoutSource_AutoDetectsJavaSelenium_AndWritesReport()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "java-auto-input");
            var outputDir = Path.Combine(temp, "generated");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "AutoDetectedJavaTests.java"), """
            package sample.tests;

            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.By;
            import org.openqa.selenium.WebDriver;

            public class AutoDetectedJavaTests {
                private WebDriver driver;

                @Test
                public void clicksSave() {
                    driver.findElement(By.id("save")).click();
                }
            }
            """);

            var result = RunCli($"--mode migrate --input \"{inputDir}\" --out \"{outputDir}\" --target ts --format both");

            AssertCliSuccess(result);
            Assert.Contains("Detected source frontend: selenium-java", result.StdOut);
            Assert.Single(Directory.GetFiles(outputDir, "*.spec.ts"));

            var reportPath = Path.Combine(outputDir, "source-detection-report.json");
            Assert.True(File.Exists(reportPath), "Expected source-detection-report.json to be written.");
            using var json = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = json.RootElement;
            Assert.Equal("source-detection/v1", root.GetProperty("SchemaVersion").GetString());
            Assert.Equal("selenium-java", root.GetProperty("SelectedSource").GetString());
            Assert.Equal("selenium-java", root.GetProperty("DetectedSourceId").GetString());
            Assert.Equal("high", root.GetProperty("Confidence").GetString());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void DumpIr_SourceAuto_DetectsPythonSelenium_V2_RecordsPythonSourceSpec()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "python-auto-input");
            var outputDir = Path.Combine(temp, "ir-dump");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "test_auto.py"), """
            from selenium.webdriver.common.by import By

            def test_auto(driver):
                driver.find_element(By.ID, "save").click()
            """);

            var result = RunCli($"--mode dump-ir --source auto --input \"{inputDir}\" --out \"{outputDir}\" --target ts --ir-version v2 --format both");

            AssertCliSuccess(result);
            Assert.Contains("Detected source frontend: selenium-python", result.StdOut);

            using var dump = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "ir-dump.json")));
            Assert.Equal("selenium-python", dump.RootElement.GetProperty("Source").GetProperty("Id").GetString());

            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "source-detection-report.json")));
            Assert.Equal("selenium-python", report.RootElement.GetProperty("SelectedSource").GetString());
            Assert.True(report.RootElement.GetProperty("FilesScanned").GetInt32() >= 1);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void Migrate_AutoDetectedJava_WritesSourceCapabilityReport()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "java-capability-input");
            var outputDir = Path.Combine(temp, "generated");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "CapabilityJavaTests.java"), """
            import org.junit.jupiter.api.Test;
            import org.openqa.selenium.By;
            import org.openqa.selenium.WebDriver;

            public class CapabilityJavaTests {
                private WebDriver driver;
                @Test public void clicksSave() {
                    driver.findElement(By.id("save")).click();
                }
            }
            """);

            var result = RunCli($"--mode migrate --input \"{inputDir}\" --out \"{outputDir}\" --target ts --format both");

            AssertCliSuccess(result);
            Assert.Contains("Source capability profile: selenium-java (experimental-mvp)", result.StdOut);

            var jsonPath = Path.Combine(outputDir, "source-capabilities-report.json");
            var mdPath = Path.Combine(outputDir, "source-capabilities-report.md");
            Assert.True(File.Exists(jsonPath), "Expected source-capabilities-report.json to be written.");
            Assert.True(File.Exists(mdPath), "Expected source-capabilities-report.md to be written.");

            using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = json.RootElement;
            Assert.Equal("source-capabilities/v1", root.GetProperty("SchemaVersion").GetString());
            Assert.Equal("selenium-java", root.GetProperty("Source").GetProperty("Id").GetString());
            Assert.Equal("experimental-mvp", root.GetProperty("Status").GetString());
            Assert.Contains(root.GetProperty("Capabilities").EnumerateArray(), c =>
                c.GetProperty("Area").GetString() == "semantic-model"
                && c.GetProperty("Support").GetString() == "none");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigNormalize_SourceJavaSelenium_WritesJavaSourceSpec()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            var outputDir = Path.Combine(temp, "normalized");
            File.WriteAllText(configPath, """
            {
              "SourceProjectName": "JavaSample",
              "Methods": []
            }
            """);

            var result = RunCli($"--mode config-normalize --source java-selenium --config \"{configPath}\" --out \"{outputDir}\" --target playwright-typescript --format json");

            AssertCliSuccess(result);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "migration-profile.v2.json")));
            var source = json.RootElement.GetProperty("Source");
            Assert.Equal("selenium-java", source.GetProperty("Id").GetString());
            Assert.Equal("java", source.GetProperty("Language").GetString());
            Assert.Equal("selenium", source.GetProperty("Framework").GetString());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void Migrate_SourcePythonSelenium_TargetTypeScript_GeneratesSpecFile()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "python-input");
            var outputDir = Path.Combine(temp, "generated");
            Directory.CreateDirectory(inputDir);
            File.WriteAllText(Path.Combine(inputDir, "test_smoke.py"), """
            from selenium.webdriver.common.by import By

            def test_smoke(driver):
                driver.get("/login")
                save = driver.find_element(By.ID, "save")
                save.click()
                driver.find_element(By.CSS_SELECTOR, ".name").send_keys("Alex")
                assert driver.find_element(By.CSS_SELECTOR, ".status").text == "Saved"
            """);

            var result = RunCli($"--mode migrate --source python-selenium --input \"{inputDir}\" --out \"{outputDir}\" --target ts --format json");

            AssertCliSuccess(result);
            var generatedFiles = Directory.GetFiles(outputDir, "*.spec.ts");
            Assert.Single(generatedFiles);

            var generated = File.ReadAllText(generatedFiles[0]);
            Assert.Contains("test('test_smoke'", generated);
            Assert.Contains("page.locator('#save')", generated);
            Assert.Contains("page.locator('.name')", generated);
            Assert.Contains("toHaveText", generated);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static void AssertCliSuccess(CliResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Expected CLI exit code 0, got {result.ExitCode}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
    }

    static CliResult RunCli(string arguments) => CliTestRunner.Run(arguments);

    static string? GetRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
            if (dir == null)
                break;
        }

        return null;
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "migrator-source-frontend-" + Guid.NewGuid().ToString("N"));
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
