using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
public class ConfigValidateTests
{
    [Fact]
    public void ConfigValidate_RegexLookingSuppressionPattern_WarnsAndMentionsHelperInventory()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "SuppressedMethodPatterns": ["\\.ElementAt\\("]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-validate --config \"{configPath}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-validate-report.md"));
            Assert.Contains("REGEX_LIKE_SUPPRESSION_PATTERN", markdown);
            Assert.Contains("glob semantics", markdown);
            Assert.Contains("--mode helper-inventory", markdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigValidate_NormalGlobSuppressionPattern_DoesNotWarnRegexLike()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "SuppressedMethodPatterns": ["*Loader.ValidateLoading(*)"]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-validate --config \"{configPath}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-validate-report.md"));
            Assert.DoesNotContain("REGEX_LIKE_SUPPRESSION_PATTERN", markdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigDiff_RegexLookingSuppressionPatternAdded_Warns()
    {
        var temp = CreateTempDir();
        try
        {
            var beforePath = Path.Combine(temp, "before.json");
            var afterPath = Path.Combine(temp, "after.json");
            File.WriteAllText(beforePath, """
            {
              "SuppressedMethodPatterns": []
            }
            """);
            File.WriteAllText(afterPath, """
            {
              "SuppressedMethodPatterns": ["\\.ElementAt\\("]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-diff --before \"{beforePath}\" --after \"{afterPath}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-diff-report.md"));
            Assert.Contains("REGEX_LIKE_SUPPRESSION_PATTERN_ADDED", markdown);
            Assert.Contains("helper-inventory", markdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }


    [Fact]
    public void ConfigValidate_StrictMode_WarnsForLegacyTargetStatementsWithoutTargetOverride()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "Verification": {},
              "Methods": [
                {
                  "SourceMethod": "page.Save.WaitVisible()",
                  "TargetStatements": [
                    "await Assertions.Expect({TARGET}).ToBeVisibleAsync();"
                  ]
                }
              ]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-validate --config \"{configPath}\" --target playwright-typescript --validation-mode strict --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-validate-report.md"));
            Assert.Contains("Validation mode: `strict`", markdown);
            Assert.Contains("TARGET_SPECIFIC_STATEMENTS_MISSING", markdown);
            Assert.Contains("playwright-typescript", markdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigValidate_ProductionMode_FailsForTypeScriptLegacyTargetStatementsWithoutOverride()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "Verification": {},
              "Methods": [
                {
                  "SourceMethod": "page.Save.WaitVisible()",
                  "TargetStatements": [
                    "await Assertions.Expect({TARGET}).ToBeVisibleAsync();"
                  ]
                }
              ]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-validate --config \"{configPath}\" --target playwright-typescript --validation-mode production --out \"{outDir}\" --format both");

            Assert.Equal(2, result.ExitCode);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-validate-report.md"));
            Assert.Contains("Validation mode: `production`", markdown);
            Assert.Contains("TS_TARGET_STATEMENTS_REQUIRED", markdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigValidate_ProductionMode_PassesForTypeScriptTargetSpecificStatements()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "Verification": {},
              "Methods": [
                {
                  "SourceMethod": "page.Save.WaitVisible()",
                  "Targets": {
                    "playwright-typescript": {
                      "TargetStatements": [
                        "await expect({TARGET}).toBeVisible();"
                      ]
                    }
                  }
                }
              ],
              "ParameterizedMethods": [
                {
                  "SourceMethodPattern": "page.Name.Set({value})",
                  "Targets": {
                    "ts": {
                      "TargetStatements": [
                        "await {TARGET}.fill({value});"
                      ]
                    }
                  }
                }
              ]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-validate --config \"{configPath}\" --target playwright-typescript --validation-mode production --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-validate-report.md"));
            Assert.Contains("Validation mode: `production`", markdown);
            Assert.DoesNotContain("TS_TARGET_STATEMENTS_REQUIRED", markdown);
            Assert.DoesNotContain("MAPPED_METHOD_REQUIRES_REVIEW", markdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigValidate_InvalidValidationMode_FailsFast()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, "{}");
            var outDir = Path.Combine(temp, "out");

            var result = RunCli($"--mode config-validate --config \"{configPath}\" --validation-mode max --out \"{outDir}\" --format json");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Invalid validation mode", result.StdErr);
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

    static CliResult RunCli(string arguments)
    {
        var repoRoot = GetRepoRoot();
        if (repoRoot == null)
            throw new InvalidOperationException("Could not find repo root (Migrator.sln not found)");

        var cliProject = Path.Combine(repoRoot, "Migrator.Cli", "Migrator.Cli.csproj");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{cliProject}\" -- {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CliResult(process.ExitCode, stdout, stderr);
    }

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
        var path = Path.Combine(Path.GetTempPath(), "migrator-config-validate-" + Guid.NewGuid().ToString("N"));
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
            // best effort cleanup
        }
    }

    record CliResult(int ExitCode, string StdOut, string StdErr);
}
