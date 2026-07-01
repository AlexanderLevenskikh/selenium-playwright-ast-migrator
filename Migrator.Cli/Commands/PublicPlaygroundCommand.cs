using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core;

internal static class PublicPlaygroundCommand
{
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

        WriteFile(Path.Combine(root, "README.md"), BuildReadme(framework, policy));
        WriteFile(Path.Combine(root, "try-this-first.md"), BuildTryThisFirst(framework, policy));
        WriteFile(Path.Combine(root, "commands.sh"), BuildCommandsSh(framework, policy));
        WriteFile(Path.Combine(root, "commands.ps1"), BuildCommandsPs1(framework, policy));
        WriteFile(Path.Combine(root, "expected-outputs.md"), BuildExpectedOutputs());
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

    static string BuildTryThisFirst(string framework, string policy) => $$"""
# Try this first: 5-minute migration playground

Run these commands from the repository root after building or installing the tool.

## 1. Generate a runbook

```bash
selenium-pw-migrator runbook \
  --input playground/selenium-csharp-nunit \
  --target dotnet \
  --target-test-framework {{framework}} \
  --generation-policy {{policy}} \
  --out playground-runbook \
  --format both
```

## 2. Check framework readiness

```bash
selenium-pw-migrator framework matrix \
  --input playground/selenium-csharp-nunit \
  --target dotnet \
  --target-test-framework {{framework}} \
  --out playground-framework-matrix \
  --format both
```

## 3. Migrate the sample

```bash
selenium-pw-migrator --mode migrate \
  --input playground/selenium-csharp-nunit \
  --config playground/configs/adapter-config.json \
  --target dotnet \
  --target-test-framework {{framework}} \
  --generation-policy {{policy}} \
  --out playground-run \
  --format both
```

## 4. Build the dashboard

```bash
selenium-pw-migrator report serve \
  --input playground-run \
  --static-only \
  --out playground-dashboard \
  --format both
```

## 5. Prepare review artifacts

```bash
selenium-pw-migrator pr pack \
  --input playground-run \
  --config playground/configs/adapter-config.json \
  --out playground-pr-pack \
  --format both

selenium-pw-migrator evidence pack \
  --input playground-run \
  --out playground-evidence.zip
```

## What good looks like

- `playground-run/report.txt` exists.
- `playground-dashboard/report-dashboard.html` opens locally.
- `playground-pr-pack/suggested-pr-description.md` is reviewable.
- `playground-evidence.zip` has a manifest and checksums.

The playground never edits source tests and never invents selectors.
""";

    static string BuildCommandsSh(string framework, string policy) => $$"""
#!/usr/bin/env bash
set -euo pipefail

selenium-pw-migrator runbook --input playground/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out playground-runbook --format both
selenium-pw-migrator framework matrix --input playground/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --out playground-framework-matrix --format both
selenium-pw-migrator --mode migrate --input playground/selenium-csharp-nunit --config playground/configs/adapter-config.json --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out playground-run --format both
selenium-pw-migrator report serve --input playground-run --static-only --out playground-dashboard --format both
selenium-pw-migrator pr pack --input playground-run --config playground/configs/adapter-config.json --out playground-pr-pack --format both
selenium-pw-migrator evidence pack --input playground-run --out playground-evidence.zip
""";

    static string BuildCommandsPs1(string framework, string policy) => $$"""
$ErrorActionPreference = "Stop"

selenium-pw-migrator runbook --input playground/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out playground-runbook --format both
selenium-pw-migrator framework matrix --input playground/selenium-csharp-nunit --target dotnet --target-test-framework {{framework}} --out playground-framework-matrix --format both
selenium-pw-migrator --mode migrate --input playground/selenium-csharp-nunit --config playground/configs/adapter-config.json --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out playground-run --format both
selenium-pw-migrator report serve --input playground-run --static-only --out playground-dashboard --format both
selenium-pw-migrator pr pack --input playground-run --config playground/configs/adapter-config.json --out playground-pr-pack --format both
selenium-pw-migrator evidence pack --input playground-run --out playground-evidence.zip
""";

    static string BuildExpectedOutputs() => """
# Expected playground outputs

After the ready commands, the workspace should contain:

```text
playground-runbook/runbook.md
playground-framework-matrix/framework-matrix.md
playground-run/report.txt
playground-run/report.json
playground-run/adapter-config.draft.json
playground-dashboard/report-dashboard.html
playground-dashboard/report-triage-decisions.md
playground-pr-pack/pr-summary.md
playground-pr-pack/reviewer-checklist.md
playground-pr-pack/suggested-pr-description.md
playground-evidence.zip
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
