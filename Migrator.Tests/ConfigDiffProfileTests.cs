using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Profiles;
using Migrator.PlaywrightTypeScript;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
public class ConfigDiffProfileTests
{
    [Fact]
    public void ConfigDiff_AdapterConfigToMigrationProfileV2_ReportsSemanticParity()
    {
        var temp = CreateTempDir();
        try
        {
            var configPath = Path.Combine(temp, "adapter-config.json");
            File.WriteAllText(configPath, """
            {
              "SourceProjectName": "Sample",
              "SourceOnlyIdentifiers": ["Urls"],
              "SuppressedMethodPatterns": ["*Loader.WaitLoaded(*)"],
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
                  ],
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
                  "SourceMethodPattern": "{source}.SetName({value})",
                  "Targets": {
                    "playwright-typescript": {
                      "TargetStatements": [
                        "await {TARGET}.fill({value});"
                      ]
                    }
                  }
                }
              ]
            }
            """);

            var normalizeOut = Path.Combine(temp, "normalize");
            AssertCliSuccess(RunCli($"--mode config-normalize --config \"{configPath}\" --target playwright-typescript --out \"{normalizeOut}\" --format both"));
            var profilePath = Path.Combine(normalizeOut, "migration-profile.v2.json");

            var diffOut = Path.Combine(temp, "diff");
            var result = RunCli($"--mode config-diff --before \"{configPath}\" --after \"{profilePath}\" --out \"{diffOut}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(diffOut, "config-diff-report.md"));
            Assert.Contains("InputKinds: adapter-config/v1 → migration-profile/v2", markdown);
            Assert.Contains("SemanticParity: passed", markdown);
            Assert.Contains("No high-risk changes detected.", markdown);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(diffOut, "config-diff-report.json")));
            Assert.Equal(0, json.RootElement.GetProperty("Changes").GetArrayLength());
            Assert.Equal(0, json.RootElement.GetProperty("Risks").GetArrayLength());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigDiff_ProfileV2WithoutLegacyConfig_UsesNormalizedSections()
    {
        var before = new ProjectAdapterConfig(
            "NoLegacy",
            new[] { new UiTargetMapping("page.Save", "save", "TestIdBeginning") },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "Urls" },
            TargetKnownTypes: new[] { "Navigation" });
        var normalized = ProjectAdapterConfigNormalizer.Normalize(
            before,
            source: new SourceSpec("selenium-csharp", "csharp", "selenium"),
            target: new PlaywrightTypeScriptBackend().Target);

        var temp = CreateTempDir();
        try
        {
            var beforePath = Path.Combine(temp, "before.json");
            var afterPath = Path.Combine(temp, "migration-profile.v2.json");
            File.WriteAllText(beforePath, System.Text.Json.JsonSerializer.Serialize(before, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(afterPath, MigrationProfileWriter.ToJson(normalized.Profile, includeLegacyConfig: false));

            var outDir = Path.Combine(temp, "diff");
            var result = RunCli($"--mode config-diff --before \"{beforePath}\" --after \"{afterPath}\" --out \"{outDir}\" --format json");

            AssertCliSuccess(result);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "config-diff-report.json")));
            Assert.Equal(0, json.RootElement.GetProperty("Changes").GetArrayLength());
            Assert.Equal(0, json.RootElement.GetProperty("Risks").GetArrayLength());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ConfigDiff_ProfileV2ChangedSemantics_ReportsChanges()
    {
        var temp = CreateTempDir();
        try
        {
            var beforePath = Path.Combine(temp, "before.json");
            File.WriteAllText(beforePath, """
            {
              "SourceProjectName": "Changed",
              "SourceOnlyIdentifiers": ["Urls"],
              "Methods": [
                {
                  "SourceMethod": "page.Save()",
                  "TargetStatements": ["await Page.GetByTestId(\"save\").ClickAsync();"]
                }
              ]
            }
            """);

            var profilePath = Path.Combine(temp, "after-profile.v2.json");
            File.WriteAllText(profilePath, """
            {
              "SchemaVersion": "migration-profile/v2",
              "SourceProjectName": "Changed",
              "Source": {
                "Id": "selenium-csharp",
                "Language": "csharp",
                "Framework": "selenium",
                "SourceOnlyIdentifiers": [],
                "SuppressedMethods": [],
                "SuppressedMethodPatterns": [],
                "RecognizerAliases": {},
                "GenericResultMethods": [],
                "WaitPolicies": []
              },
              "Target": {
                "Id": "playwright-typescript",
                "Language": "typescript",
                "Framework": "playwright",
                "TargetKnownTypes": [],
                "TargetKnownIdentifiers": [],
                "TargetStatementDefaults": {}
              },
              "Project": {
                "UiTargets": [],
                "PageObjects": [],
                "Methods": [],
                "ParameterizedMethods": [],
                "Tables": [],
                "Pagination": [],
                "NavigationUrls": {},
                "Scopes": []
              }
            }
            """);

            var outDir = Path.Combine(temp, "diff");
            var result = RunCli($"--mode config-diff --before \"{beforePath}\" --after \"{profilePath}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            var markdown = File.ReadAllText(Path.Combine(outDir, "config-diff-report.md"));
            Assert.Contains("SemanticParity: changed", markdown);
            Assert.Contains("SourceOnlyIdentifiers: **removed** `Urls`", markdown);
            Assert.Contains("Methods: **removed** `page.Save()`", markdown);
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
        var path = Path.Combine(Path.GetTempPath(), "migrator-config-diff-profile-" + Guid.NewGuid().ToString("N"));
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
}
