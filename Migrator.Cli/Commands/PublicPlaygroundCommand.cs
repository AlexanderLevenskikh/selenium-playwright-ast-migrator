using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core;

internal static class PublicPlaygroundCommand
{
    static readonly string[] PlaygroundRequiredFiles =
    {
        "README.md",
        "try-this-first.md",
        "commands.sh",
        "commands.ps1",
        "expected-outputs.md",
        "reviewer-demo-checklist.md",
        "playground-manifest.json",
        "selenium-csharp-nunit/LoginSmokeTest.cs",
        "configs/adapter-config.json",
        "sample-artifacts/dashboard/report-dashboard.md",
        "sample-artifacts/dashboard/report-dashboard.html",
        "sample-artifacts/pr-pack/suggested-pr-description.md",
    };

    public static int RunPlayground(string outPath, string format, string targetTestFramework, string generationPolicy)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(outPath) ? Path.Combine("migration", "playground") : outPath);
        Directory.CreateDirectory(root);

        var sourceDir = Path.Combine(root, "selenium-csharp-nunit");
        var configDir = Path.Combine(root, "configs");
        var expectedDir = Path.Combine(root, "expected-playwright-dotnet");
        var artifactsDir = Path.Combine(root, "sample-artifacts");
        var dashboardDir = Path.Combine(artifactsDir, "dashboard");
        var prPackDir = Path.Combine(artifactsDir, "pr-pack");

        foreach (var dir in new[] { sourceDir, configDir, expectedDir, dashboardDir, prPackDir })
            Directory.CreateDirectory(dir);

        var framework = NormalizeFramework(targetTestFramework);
        var policy = GenerationPolicy.NormalizeOrDefault(generationPolicy);

        var commandRoot = ToCommandPath(root);
        WriteFile(Path.Combine(root, "README.md"), BuildReadme(framework, policy));
        WriteFile(Path.Combine(root, "try-this-first.md"), BuildTryThisFirst(framework, policy, commandRoot));
        WriteFile(Path.Combine(root, "commands.sh"), BuildCommandsSh(framework, policy, commandRoot));
        WriteFile(Path.Combine(root, "commands.ps1"), BuildCommandsPs1(framework, policy, commandRoot));
        WriteFile(Path.Combine(root, "expected-outputs.md"), BuildExpectedOutputs(commandRoot));
        WriteFile(Path.Combine(root, "reviewer-demo-checklist.md"), BuildReviewerChecklist());
        WriteFile(Path.Combine(sourceDir, "LoginSmokeTest.cs"), BuildSeleniumNUnitSource());
        WriteFile(Path.Combine(configDir, "adapter-config.json"), BuildAdapterConfig(framework, policy));
        WriteFile(Path.Combine(expectedDir, framework == "xunit" ? "LoginSmokeFactsPlaywright.generated.cs" : "LoginSmokeTestPlaywright.generated.cs"), BuildExpectedGenerated(framework));
        WriteFile(Path.Combine(dashboardDir, "report-dashboard.md"), BuildDashboardMarkdown());
        WriteFile(Path.Combine(dashboardDir, "report-dashboard.html"), BuildDashboardHtml());
        WriteFile(Path.Combine(prPackDir, "suggested-pr-description.md"), BuildSuggestedPrDescription());

        var manifest = new
        {
            SchemaVersion = "public-playground/v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Target = "dotnet",
            TargetTestFramework = framework,
            GenerationPolicy = policy,
            ReadOnly = true,
            TryThisFirst = "try-this-first.md",
            Commands = new[]
            {
                "runbook",
                "framework matrix",
                "migrate",
                "report serve --static-only",
                "pr pack",
                "evidence pack"
            },
            Files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        if (format == "json" || format == "both")
        {
            WriteFile(Path.Combine(root, "playground-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }

        Console.WriteLine("=== Public Playground ===");
        Console.WriteLine($"Output: {root}");
        Console.WriteLine($"Target framework: {framework}");
        Console.WriteLine($"Generation policy: {policy}");
        Console.WriteLine("Start with: try-this-first.md");
        return 0;
    }

    public static int RunVerifyPlayground(string inputPath, string outPath, string format)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(inputPath) ? "playground" : inputPath);
        Directory.CreateDirectory(outPath);

        var checks = new List<PlaygroundVerifyCheck>();
        Add(checks, Directory.Exists(root), "root", ".", "playground root exists", $"playground root does not exist: {root}");

        foreach (var file in PlaygroundRequiredFiles)
        {
            var fullPath = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Add(checks, File.Exists(fullPath), "file", file, "required playground file exists", "required playground file is missing");
        }

        AddContentCheck(checks, root, "try-this-first.md", "runbook", "try-this-first includes runbook step");
        AddContentCheck(checks, root, "try-this-first.md", "framework matrix", "try-this-first includes framework matrix step");
        AddContentCheck(checks, root, "try-this-first.md", "--mode migrate", "try-this-first includes migrate step");
        AddContentCheck(checks, root, "try-this-first.md", "report serve", "try-this-first includes dashboard step");
        AddContentCheck(checks, root, "try-this-first.md", "pr pack", "try-this-first includes PR pack step");
        AddContentCheck(checks, root, "try-this-first.md", "evidence pack", "try-this-first includes evidence pack step");
        AddContentCheck(checks, root, "try-this-first.md", "never invents selectors", "try-this-first keeps selector-safety warning");
        AddContentCheck(checks, root, "commands.sh", "set -euo pipefail", "bash command chain fails fast");
        AddContentCheck(checks, root, "configs/adapter-config.json", "adapter-config/v1", "adapter config declares schema version");

        var expectedGeneratedExists = Directory.Exists(Path.Combine(root, "expected-playwright-dotnet"))
            && Directory.EnumerateFiles(Path.Combine(root, "expected-playwright-dotnet"), "*.generated.cs", SearchOption.TopDirectoryOnly).Any();
        Add(checks, expectedGeneratedExists, "file", "expected-playwright-dotnet/*.generated.cs", "expected generated Playwright file exists", "expected generated Playwright file is missing");

        var manifestPath = Path.Combine(root, "playground-manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var schemaVersion = document.RootElement.TryGetProperty("SchemaVersion", out var schema) ? schema.GetString() : null;
                Add(checks, string.Equals(schemaVersion, "public-playground/v1", StringComparison.Ordinal), "manifest", "SchemaVersion", "manifest schema version is public-playground/v1", $"manifest schema version is {schemaVersion ?? "missing"}");
                var readOnly = document.RootElement.TryGetProperty("ReadOnly", out var ro) && ro.ValueKind == JsonValueKind.True;
                Add(checks, readOnly, "manifest", "ReadOnly", "manifest marks playground as read-only", "manifest should set ReadOnly=true");
            }
            catch (JsonException ex)
            {
                Add(checks, false, "manifest", "playground-manifest.json", "manifest parses", $"manifest JSON parse failed: {ex.Message}");
            }
        }

        var failed = checks.Count(c => c.Status == "fail");
        var report = new PlaygroundVerifyReport(
            "public-playground-verify/v1",
            DateTimeOffset.UtcNow,
            root,
            failed == 0 ? "passed" : "failed",
            failed,
            checks.ToArray());

        if (format == "text" || format == "both")
            WriteFile(Path.Combine(outPath, "playground-verify-report.md"), BuildVerifyMarkdown(report));
        if (format == "json" || format == "both")
            WriteFile(Path.Combine(outPath, "playground-verify-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        Console.WriteLine($"Playground verify: {report.Status} ({failed} failed)");
        Console.WriteLine($"Report: {Path.GetFullPath(outPath)}");
        return failed == 0 ? 0 : 2;
    }

    static string NormalizeFramework(string? framework)
    {
        if (string.Equals(framework, "xunit", StringComparison.OrdinalIgnoreCase))
            return "xunit";
        return "nunit";
    }

    static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    static void AddContentCheck(List<PlaygroundVerifyCheck> checks, string root, string relativePath, string expected, string ok)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            Add(checks, false, "content", relativePath, ok, "file missing; content could not be checked");
            return;
        }

        var text = File.ReadAllText(path);
        Add(checks, text.Contains(expected, StringComparison.OrdinalIgnoreCase), "content", relativePath, ok, $"expected content not found: {expected}");
    }

    static void Add(List<PlaygroundVerifyCheck> checks, bool condition, string category, string item, string ok, string fail)
    {
        checks.Add(new PlaygroundVerifyCheck(category, item, condition ? "pass" : "fail", condition ? ok : fail));
    }

    static string BuildVerifyMarkdown(PlaygroundVerifyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Playground Verify Report");
        sb.AppendLine();
        sb.AppendLine($"Status: **{report.Status}**");
        sb.AppendLine($"Playground root: `{report.PlaygroundRoot}`");
        sb.AppendLine($"Generated at: `{report.GeneratedAtUtc:O}`");
        sb.AppendLine();
        sb.AppendLine($"Failed checks: {report.FailedChecks}");
        sb.AppendLine();
        sb.AppendLine("| Status | Category | Item | Message |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var check in report.Checks.OrderBy(c => c.Status).ThenBy(c => c.Category).ThenBy(c => c.Item, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| {Escape(check.Status)} | {Escape(check.Category)} | `{Escape(check.Item)}` | {Escape(check.Message)} |");
        return sb.ToString();
    }

    static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    static string BuildReadme(string framework, string policy) => $$"""
# Selenium Playwright Migrator Public Playground

This playground is a five-minute demo workspace. It is intentionally tiny, public-safe, and self-contained.

Start here:

```bash
cat try-this-first.md
```

The playground shows the production happy path:

1. generate a migration runbook;
2. inspect framework readiness;
3. migrate a tiny Selenium C# NUnit smoke test;
4. generate a static dashboard;
5. prepare a PR pack;
6. create an evidence pack.

Selected target test framework: `{{framework}}`.
Selected generation policy: `{{policy}}`.

## Files

- `selenium-csharp-nunit/LoginSmokeTest.cs` — sample Selenium input.
- `configs/adapter-config.json` — starter adapter config.
- `expected-playwright-dotnet/` — expected generated Playwright output shape.
- `sample-artifacts/dashboard/` — static dashboard sample.
- `sample-artifacts/pr-pack/` — suggested PR description sample.
- `commands.sh` / `commands.ps1` — ready commands.
- `expected-outputs.md` — what should appear after the commands.

This playground is read-only with respect to your real project: it writes only inside the selected playground output directory.
""";

    static string ToCommandPath(string path)
    {
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        var commandPath = relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
        return commandPath.Replace('\\', '/');
    }

    static string BuildTryThisFirst(string framework, string policy, string playgroundRoot) => $$"""
# Try this first: 5-minute migration playground

Run these commands from the repository root after building or installing the tool.

This playground was generated at `{{playgroundRoot}}`. The commands below use that path explicitly.

## 1. Generate a runbook

```bash
selenium-pw-migrator runbook \
  --input {{playgroundRoot}}/selenium-csharp-nunit \
  --target dotnet \
  --target-test-framework {{framework}} \
  --generation-policy {{policy}} \
  --out migration/playground-runbook \
  --format both
```

## 2. Check framework readiness

```bash
selenium-pw-migrator framework matrix \
  --input {{playgroundRoot}}/selenium-csharp-nunit \
  --target dotnet \
  --target-test-framework {{framework}} \
  --out migration/playground-framework-matrix \
  --format both
```

## 3. Migrate the sample

```bash
selenium-pw-migrator --mode migrate \
  --input {{playgroundRoot}}/selenium-csharp-nunit \
  --config {{playgroundRoot}}/configs/adapter-config.json \
  --target dotnet \
  --target-test-framework {{framework}} \
  --generation-policy {{policy}} \
  --out migration/playground-run \
  --format both
```

## 4. Build the dashboard

```bash
selenium-pw-migrator report serve \
  --input migration/playground-run \
  --static-only \
  --out migration/playground-dashboard \
  --format both
```

## 5. Prepare review artifacts

```bash
selenium-pw-migrator pr pack \
  --input migration/playground-run \
  --config {{playgroundRoot}}/configs/adapter-config.json \
  --out migration/playground-pr-pack \
  --format both

selenium-pw-migrator evidence pack \
  --input migration/playground-run \
  --out migration/playground-evidence.zip
```

## What good looks like

- `migration/playground-run/report.txt` exists.
- `migration/playground-dashboard/report-dashboard.html` opens locally.
- `migration/playground-pr-pack/suggested-pr-description.md` is reviewable.
- `migration/playground-evidence.zip` has a manifest and checksums.

The playground never edits source tests and never invents selectors.
""";

    static string BuildCommandsSh(string framework, string policy, string playgroundRoot) => $$"""
#!/usr/bin/env bash
set -euo pipefail

selenium-pw-migrator runbook --input {{playgroundRoot}}/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out migration/playground-runbook --format both
selenium-pw-migrator framework matrix --input {{playgroundRoot}}/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --out migration/playground-framework-matrix --format both
selenium-pw-migrator --mode migrate --input {{playgroundRoot}}/selenium-csharp-nunit --config {{playgroundRoot}}/configs/adapter-config.json --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out migration/playground-run --format both
selenium-pw-migrator report serve --input migration/playground-run --static-only --out migration/playground-dashboard --format both
selenium-pw-migrator pr pack --input migration/playground-run --config {{playgroundRoot}}/configs/adapter-config.json --out migration/playground-pr-pack --format both
selenium-pw-migrator evidence pack --input migration/playground-run --out migration/playground-evidence.zip
""";

    static string BuildCommandsPs1(string framework, string policy, string playgroundRoot) => $$"""
$ErrorActionPreference = "Stop"

selenium-pw-migrator runbook --input {{playgroundRoot}}/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out migration/playground-runbook --format both
selenium-pw-migrator framework matrix --input {{playgroundRoot}}/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --out migration/playground-framework-matrix --format both
selenium-pw-migrator --mode migrate --input {{playgroundRoot}}/selenium-csharp-nunit --config {{playgroundRoot}}/configs/adapter-config.json --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out migration/playground-run --format both
selenium-pw-migrator report serve --input migration/playground-run --static-only --out migration/playground-dashboard --format both
selenium-pw-migrator pr pack --input migration/playground-run --config {{playgroundRoot}}/configs/adapter-config.json --out migration/playground-pr-pack --format both
selenium-pw-migrator evidence pack --input migration/playground-run --out migration/playground-evidence.zip
""";

    static string BuildExpectedOutputs(string playgroundRoot) => $$"""
# Expected playground outputs

After the ready commands, the workspace should contain:

```text
{{playgroundRoot}}/
migration/playground-runbook/runbook.md
migration/playground-framework-matrix/framework-matrix.md
migration/playground-run/report.txt
migration/playground-run/report.json
migration/playground-run/adapter-config.draft.json
migration/playground-dashboard/report-dashboard.html
migration/playground-dashboard/report-triage-decisions.md
migration/playground-pr-pack/pr-summary.md
migration/playground-pr-pack/reviewer-checklist.md
migration/playground-pr-pack/suggested-pr-description.md
migration/playground-evidence.zip
```

The demo is successful when the dashboard and PR pack can be reviewed without opening the source project.
""";

    static string BuildReviewerChecklist() => """
# Playground reviewer checklist

- [ ] Generated files contain only public demo names.
- [ ] Report and dashboard are present.
- [ ] Selector evidence is either explicit or marked for review.
- [ ] No source tests were modified.
- [ ] Evidence pack contains manifest and checksums.
""";

    static string BuildSeleniumNUnitSource() => """
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace PublicDemo.Selenium;

public class LoginSmokeTest
{
    private IWebDriver driver = null!;

    [SetUp]
    public void SetUp()
    {
        driver = new ChromeDriver();
    }

    [Test]
    public void UserCanSubmitLoginForm()
    {
        driver.Navigate().GoToUrl("https://example.test/login");
        driver.FindElement(By.Id("login")).SendKeys("demo@example.test");
        driver.FindElement(By.Id("password")).SendKeys("secret");
        driver.FindElement(By.CssSelector("button[type='submit']")).Click();
        Assert.That(driver.FindElement(By.Id("status")).Text, Is.EqualTo("Signed in"));
    }
}
""";

    static string BuildAdapterConfig(string framework, string policy) => $$"""
{
  "SchemaVersion": "adapter-config/v1",
  "GenerationPolicy": "{{policy}}",
  "LocatorSettings": {
    "DefaultTestIdAttribute": "data-testid"
  },
  "TestHost": {
    "TargetTestFramework": "{{framework}}",
    "Namespace": "PublicDemo.Playwright",
    "BaseClass": "PageTest"
  },
  "UiTargets": {
    "login": {
      "Kind": "CssSelector",
      "TargetExpression": "#login"
    },
    "password": {
      "Kind": "CssSelector",
      "TargetExpression": "#password"
    },
    "button[type='submit']": {
      "Kind": "CssSelector",
      "TargetExpression": "button[type='submit']"
    },
    "status": {
      "Kind": "CssSelector",
      "TargetExpression": "#status"
    }
  }
}
""";

    static string BuildExpectedGenerated(string framework)
    {
        if (framework == "xunit")
        {
            return """
// Generated by Migrator — Selenium C# → Playwright .NET
using Microsoft.Playwright;
using Microsoft.Playwright.Extensions.Xunit;
using Xunit;

namespace PublicDemo.Playwright;

[Trait("migration", "public-playground")]
public class LoginSmokeFacts : PageTest, IAsyncLifetime
{
    [Fact]
    public async Task UserCanSubmitLoginForm()
    {
        await Page.GotoAsync("https://example.test/login");
        await Page.Locator("#login").FillAsync("demo@example.test");
        await Page.Locator("#password").FillAsync("secret");
        await Page.Locator("button[type='submit']").ClickAsync();
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Signed in");
    }
}
""";
        }

        return """
// Generated by Migrator — Selenium C# → Playwright .NET
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace PublicDemo.Playwright;

[TestFixture]
public class LoginSmokeTestPlaywright : PageTest
{
    [Test]
    public async Task UserCanSubmitLoginForm()
    {
        await Page.GotoAsync("https://example.test/login");
        await Page.Locator("#login").FillAsync("demo@example.test");
        await Page.Locator("#password").FillAsync("secret");
        await Page.Locator("button[type='submit']").ClickAsync();
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Signed in");
    }
}
""";
    }

    static string BuildDashboardMarkdown() => """
# Playground Dashboard Sample

## Overview

- Files: 1
- Tests: 1
- Generated files: 1
- TODOs: 0
- Unsupported actions: 0
- Runtime readiness score: 100

## Quality trend

The public playground represents the expected green path for a tiny pilot scope.

## Triage decisions

| Decision | Item | Reason |
|---|---|---|
| accept | public playground smoke | Selectors are explicit and generated output is reviewable. |
""";

    static string BuildDashboardHtml() => """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Selenium Playwright Migrator Playground Dashboard</title>
</head>
<body>
  <main>
    <h1>Playground Dashboard</h1>
    <p>What good looks like: one generated smoke test, zero unsupported actions, reviewable evidence.</p>
    <h2>Quality trend</h2>
    <p>Stable green sample for public demo use.</p>
    <h2>Triage decisions</h2>
    <ul><li><strong>accept</strong>: public playground smoke</li></ul>
  </main>
</body>
</html>
""";

    static string BuildSuggestedPrDescription() => """
# Suggested PR description

## Summary

Adds a tiny Selenium C# to Playwright .NET playground migration sample.

## Evidence

- Runbook reviewed.
- Dashboard generated.
- PR pack generated.
- Evidence pack created.

## Reviewer checklist

- [ ] Generated output matches the source smoke scenario.
- [ ] No selectors were invented.
- [ ] Evidence artifacts are attached or linked.
""";
}

internal sealed record PlaygroundVerifyReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string PlaygroundRoot,
    string Status,
    int FailedChecks,
    PlaygroundVerifyCheck[] Checks);

internal sealed record PlaygroundVerifyCheck(string Category, string Item, string Status, string Message);
