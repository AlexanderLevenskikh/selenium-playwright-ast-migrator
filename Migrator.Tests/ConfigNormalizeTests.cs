using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Profiles;
using Migrator.PlaywrightTypeScript;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
public class ConfigNormalizeTests
{
    [Fact]
    public void ConfigNormalize_WritesMigrationProfileV2AndReport()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "SourceProjectName": "Sample",
              "SourceOnlyIdentifiers": ["Urls"],
              "TargetKnownTypes": ["Navigation"],
              "UiTargets": [
                {
                  "SourceExpression": "page.SaveButton",
                  "TargetExpression": "save-button",
                  "TargetKind": "TestIdBeginning"
                }
              ],
              "Methods": [
                {
                  "SourceMethod": "page.SaveButton.WaitVisible()",
                  "TargetStatements": [
                    "await Assertions.Expect({TARGET}).ToBeVisibleAsync();"
                  ]
                }
              ]
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-normalize --config \"{configPath}\" --out \"{outDir}\" --format both --target playwright-typescript");

            AssertCliSuccess(result);

            var profilePath = Path.Combine(outDir, "migration-profile.v2.json");
            var reportJsonPath = Path.Combine(outDir, "config-normalize-report.json");
            var reportMdPath = Path.Combine(outDir, "config-normalize-report.md");

            Assert.True(File.Exists(profilePath), "Expected migration-profile.v2.json to be written.");
            Assert.True(File.Exists(reportJsonPath), "Expected config-normalize-report.json to be written.");
            Assert.True(File.Exists(reportMdPath), "Expected config-normalize-report.md to be written.");

            using var profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            var root = profile.RootElement;
            Assert.Equal("migration-profile/v2", root.GetProperty("SchemaVersion").GetString());
            Assert.Equal("Sample", root.GetProperty("SourceProjectName").GetString());
            Assert.Equal("selenium-csharp", root.GetProperty("Source").GetProperty("Id").GetString());
            Assert.Equal("csharp", root.GetProperty("Source").GetProperty("Language").GetString());
            Assert.Equal("playwright-typescript", root.GetProperty("Target").GetProperty("Id").GetString());
            Assert.Equal("typescript", root.GetProperty("Target").GetProperty("Language").GetString());
            Assert.Equal(1, root.GetProperty("Project").GetProperty("UiTargets").GetArrayLength());
            Assert.Equal(1, root.GetProperty("Project").GetProperty("Methods").GetArrayLength());
            Assert.True(root.TryGetProperty("LegacyConfig", out _));

            var reportMarkdown = File.ReadAllText(reportMdPath);
            Assert.Contains("# Config Normalize Report", reportMarkdown);
            Assert.Contains("CONFIG_V1_LEGACY_TARGET_STATEMENTS", reportMarkdown);
            Assert.Contains("migration-profile.v2.json", reportMarkdown);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigNormalize_AcceptsInputPathWhenConfigFlagIsOmitted()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "SourceProjectName": "InputOnly",
              "Methods": []
            }
            """);

            var outDir = Path.Combine(temp, "out");
            var result = RunCli($"--mode config-normalize --input \"{configPath}\" --out \"{outDir}\" --format json");

            AssertCliSuccess(result);
            Assert.True(File.Exists(Path.Combine(outDir, "migration-profile.v2.json")));
            Assert.True(File.Exists(Path.Combine(outDir, "config-normalize-report.json")));
            Assert.False(File.Exists(Path.Combine(outDir, "config-normalize-report.md")));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void MigrationProfileWriter_FlattensSourceAndTargetSpecsForExternalSchema()
    {
        var config = new ProjectAdapterConfig(
            "WriterSample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "Urls" },
            TargetKnownIdentifiers: new[] { "test" });

        var normalized = ProjectAdapterConfigNormalizer.Normalize(
            config,
            source: new SourceSpec("selenium-csharp", "csharp", "selenium"),
            target: new PlaywrightTypeScriptBackend().Target);

        var json = MigrationProfileWriter.ToJson(normalized.Profile);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        Assert.Equal("selenium-csharp", root.GetProperty("Source").GetProperty("Id").GetString());
        Assert.False(root.GetProperty("Source").TryGetProperty("Source", out _));
        Assert.Equal("playwright-typescript", root.GetProperty("Target").GetProperty("Id").GetString());
        Assert.False(root.GetProperty("Target").TryGetProperty("Target", out _));
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
        var path = Path.Combine(Path.GetTempPath(), "migrator-config-normalize-" + Guid.NewGuid().ToString("N"));
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
