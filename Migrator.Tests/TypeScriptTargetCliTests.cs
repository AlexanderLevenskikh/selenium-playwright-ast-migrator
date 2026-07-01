using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
public class TypeScriptTargetCliTests
{
    [Fact]
    public void Migrate_TargetTs_DoesNotRequireTsProjectForGeneration()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "input");
            var outputDir = Path.Combine(temp, "generated");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "SmokeTests.cs"), """
            using NUnit.Framework;
            using OpenQA.Selenium;

            namespace Sample.Tests;

            public class SmokeTests
            {
                private IWebDriver Driver = null!;

                [Test]
                public void OpensHome()
                {
                    Driver.Navigate().GoToUrl("/home");
                    Driver.FindElement(By.CssSelector("#save")).Click();
                }
            }
            """);

            var result = RunCli($"--mode migrate --input \"{inputDir}\" --out \"{outputDir}\" --target ts --format both");

            AssertCliSuccess(result);
            Assert.DoesNotContain("--ts-project", result.StdErr);

            var generatedFiles = Directory.GetFiles(outputDir, "*.spec.ts");
            Assert.Single(generatedFiles);

            var generated = File.ReadAllText(generatedFiles[0]);
            Assert.Contains("import { test, expect } from '@playwright/test';", generated);
            Assert.Contains("test('OpensHome'", generated);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void Migrate_TargetStablePlaywrightTypeScriptId_IsAccepted()
    {
        var temp = CreateTempDir();
        try
        {
            var inputDir = Path.Combine(temp, "input");
            var outputDir = Path.Combine(temp, "generated");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "EmptyTests.cs"), """
            using NUnit.Framework;

            namespace Sample.Tests;

            public class EmptyTests
            {
                [Test]
                public void T1()
                {
                }
            }
            """);

            var result = RunCli($"--mode migrate --input \"{inputDir}\" --out \"{outputDir}\" --target playwright-typescript --format both");

            AssertCliSuccess(result);
            Assert.Single(Directory.GetFiles(outputDir, "*.spec.ts"));
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
        var path = Path.Combine(Path.GetTempPath(), "migrator-ts-target-" + Guid.NewGuid().ToString("N"));
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
