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
        "app/index.html",
        "selenium-csharp-nunit/LoginSmokeTest.cs",
        "configs/adapter-config.json",
        "sample-artifacts/dashboard/report-dashboard.md",
        "sample-artifacts/dashboard/report-dashboard.html",
        "sample-artifacts/pr-pack/suggested-pr-description.md",
        "playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj",
        "playwright-dotnet-proof/StaticAppSmokeTests.cs",
    };

    public static int RunPlayground(string outPath, string format, string targetTestFramework, string generationPolicy)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(outPath) ? Path.Combine("migration", "playground") : outPath);
        Directory.CreateDirectory(root);

        var appDir = Path.Combine(root, "app");
        var sourceDir = Path.Combine(root, "selenium-csharp-nunit");
        var configDir = Path.Combine(root, "configs");
        var expectedDir = Path.Combine(root, "expected-playwright-dotnet");
        var proofDir = Path.Combine(root, "playwright-dotnet-proof");
        var artifactsDir = Path.Combine(root, "sample-artifacts");
        var dashboardDir = Path.Combine(artifactsDir, "dashboard");
        var prPackDir = Path.Combine(artifactsDir, "pr-pack");

        foreach (var dir in new[] { appDir, sourceDir, configDir, expectedDir, proofDir, dashboardDir, prPackDir })
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
        WriteFile(Path.Combine(appDir, "index.html"), BuildStaticAppHtml());
        WriteFile(Path.Combine(sourceDir, "LoginSmokeTest.cs"), BuildSeleniumNUnitSource());
        WriteFile(Path.Combine(configDir, "adapter-config.json"), BuildAdapterConfig(framework, policy));
        WriteFile(Path.Combine(expectedDir, framework == "xunit" ? "LoginSmokeFactsPlaywright.generated.cs" : "LoginSmokeTestPlaywright.generated.cs"), BuildExpectedGenerated(framework));
        WriteFile(Path.Combine(proofDir, "PublicDemo.PlaywrightProof.csproj"), BuildPlaywrightProofCsproj());
        WriteFile(Path.Combine(proofDir, "StaticAppSmokeTests.cs"), BuildPlaywrightProofTest());
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
        AddContentCheck(checks, root, "app/index.html", "data-testid=\"login-username\"", "static fake app contains login username test id");
        AddContentCheck(checks, root, "app/index.html", "data-testid=\"orders-status\"", "static fake app contains orders status test id");
        AddContentCheck(checks, root, "playwright-dotnet-proof/StaticAppSmokeTests.cs", "Page.GetByTestId(\"catalog-add-mug\")", "Playwright proof exercises catalog/cart flow");
        AddContentCheck(checks, root, "playwright-dotnet-proof/StaticAppSmokeTests.cs", "app/index.html", "Playwright proof opens static fake app");

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

1. inspect a static fake app with stable `data-testid` controls;
2. generate a migration runbook;
3. inspect framework readiness;
4. migrate a tiny Selenium C# NUnit smoke test;
5. generate a static dashboard;
6. prepare a PR pack;
7. create an evidence pack;
8. optionally run a Playwright proof against `app/index.html`.

Selected target test framework: `{{framework}}`.
Selected generation policy: `{{policy}}`.

## Files

- `app/index.html` — static fake web app; no backend, Docker, npm, or Selenium runtime required.
- `selenium-csharp-nunit/LoginSmokeTest.cs` — sample Selenium input.
- `configs/adapter-config.json` — starter adapter config.
- `expected-playwright-dotnet/` — expected generated Playwright output shape.
- `playwright-dotnet-proof/` — optional runtime proof against the static app.
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

This playground was generated at `{{playgroundRoot}}`. The commands below keep all generated artifacts under `{{playgroundRoot}}/runs`, so custom `--out` paths stay self-contained.

## Bash happy path

```bash
bash {{playgroundRoot}}/commands.sh
```

## Windows PowerShell happy path

```powershell
./{{playgroundRoot}}/commands.ps1
```

## Ready command chain

The generated scripts run the same sequence: `runbook`, `framework matrix`, `--mode migrate`, `report serve`, `pr pack`, and `evidence pack`.

## What good looks like

- `{{playgroundRoot}}/runs/playground-run/report.txt` exists.
- `{{playgroundRoot}}/runs/playground-dashboard/report-dashboard.html` opens locally.
- `{{playgroundRoot}}/runs/playground-pr-pack/suggested-pr-description.md` is reviewable.
- `{{playgroundRoot}}/runs/playground-evidence.zip` has a manifest and checksums.
- `{{playgroundRoot}}/app/index.html` can be opened directly in a browser.
- Optional: `dotnet test {{playgroundRoot}}/playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj` proves the target Playwright selectors against the fake app.

The playground never edits source tests and never invents selectors.
""";

    static string BuildCommandsSh(string framework, string policy, string playgroundRoot) => $$"""
#!/usr/bin/env bash
set -euo pipefail

PLAYGROUND_ROOT="{{playgroundRoot}}"
RUN_ROOT="$PLAYGROUND_ROOT/runs"
mkdir -p "$RUN_ROOT"

selenium-pw-migrator runbook --input "$PLAYGROUND_ROOT/selenium-csharp-nunit" --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out "$RUN_ROOT/playground-runbook" --format both
selenium-pw-migrator framework matrix --input "$PLAYGROUND_ROOT/selenium-csharp-nunit" --target dotnet --target-test-framework {{framework}} --out "$RUN_ROOT/playground-framework-matrix" --format both
selenium-pw-migrator --mode migrate --input "$PLAYGROUND_ROOT/selenium-csharp-nunit" --config "$PLAYGROUND_ROOT/configs/adapter-config.json" --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out "$RUN_ROOT/playground-run" --format both
selenium-pw-migrator report serve --input "$RUN_ROOT/playground-run" --static-only --out "$RUN_ROOT/playground-dashboard" --format both
selenium-pw-migrator pr pack --input "$RUN_ROOT/playground-run" --config "$PLAYGROUND_ROOT/configs/adapter-config.json" --out "$RUN_ROOT/playground-pr-pack" --format both
selenium-pw-migrator evidence pack --input "$RUN_ROOT/playground-run" --out "$RUN_ROOT/playground-evidence.zip"
""";

    static string BuildCommandsPs1(string framework, string policy, string playgroundRoot) => $$"""
$ErrorActionPreference = "Stop"

$PlaygroundRoot = "{{playgroundRoot}}"
$RunRoot = Join-Path $PlaygroundRoot "runs"
New-Item -ItemType Directory -Force -Path $RunRoot | Out-Null

selenium-pw-migrator runbook --input (Join-Path $PlaygroundRoot "selenium-csharp-nunit") --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out (Join-Path $RunRoot "playground-runbook") --format both
selenium-pw-migrator framework matrix --input (Join-Path $PlaygroundRoot "selenium-csharp-nunit") --target dotnet --target-test-framework {{framework}} --out (Join-Path $RunRoot "playground-framework-matrix") --format both
selenium-pw-migrator --mode migrate --input (Join-Path $PlaygroundRoot "selenium-csharp-nunit") --config (Join-Path $PlaygroundRoot "configs/adapter-config.json") --target dotnet --target-test-framework {{framework}} --generation-policy {{policy}} --out (Join-Path $RunRoot "playground-run") --format both
selenium-pw-migrator report serve --input (Join-Path $RunRoot "playground-run") --static-only --out (Join-Path $RunRoot "playground-dashboard") --format both
selenium-pw-migrator pr pack --input (Join-Path $RunRoot "playground-run") --config (Join-Path $PlaygroundRoot "configs/adapter-config.json") --out (Join-Path $RunRoot "playground-pr-pack") --format both
selenium-pw-migrator evidence pack --input (Join-Path $RunRoot "playground-run") --out (Join-Path $RunRoot "playground-evidence.zip")
""";

    static string BuildExpectedOutputs(string playgroundRoot) => $$"""
# Expected playground outputs

After the ready commands, the workspace should contain:

```text
{{playgroundRoot}}/
{{playgroundRoot}}/runs/playground-runbook/runbook.md
{{playgroundRoot}}/runs/playground-framework-matrix/framework-matrix.md
{{playgroundRoot}}/runs/playground-run/report.txt
{{playgroundRoot}}/runs/playground-run/report.json
{{playgroundRoot}}/runs/playground-run/adapter-config.draft.json
{{playgroundRoot}}/runs/playground-dashboard/report-dashboard.html
{{playgroundRoot}}/runs/playground-dashboard/report-triage-decisions.md
{{playgroundRoot}}/runs/playground-pr-pack/pr-summary.md
{{playgroundRoot}}/runs/playground-pr-pack/reviewer-checklist.md
{{playgroundRoot}}/runs/playground-pr-pack/suggested-pr-description.md
{{playgroundRoot}}/runs/playground-evidence.zip
{{playgroundRoot}}/app/index.html
{{playgroundRoot}}/playwright-dotnet-proof/StaticAppSmokeTests.cs
```

The demo is successful when the dashboard and PR pack can be reviewed without opening the source project. The optional Playwright proof can additionally run against the static fake app.
""";

    static string BuildReviewerChecklist() => """
# Playground reviewer checklist

- [ ] Generated files contain only public demo names.
- [ ] Report and dashboard are present.
- [ ] Selector evidence is either explicit or marked for review.
- [ ] No source tests were modified.
- [ ] Evidence pack contains manifest and checksums.
- [ ] Static app contains every data-testid used by generated output.
- [ ] Optional Playwright proof can run without Selenium or ChromeDriver.
""";


    static string BuildStaticAppHtml() => """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Migrator Playground Demo Shop</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f6f8fb; color: #172033; }
    main { width: min(900px, calc(100vw - 32px)); background: white; border: 1px solid #d8e0ee; border-radius: 20px; padding: 28px; box-shadow: 0 24px 80px rgba(23, 32, 51, .10); }
    label { display: grid; gap: 6px; margin: 12px 0; font-weight: 600; }
    input { border: 1px solid #c9d4e5; border-radius: 12px; padding: 12px 14px; font: inherit; }
    button { border: 0; border-radius: 12px; padding: 12px 16px; font: inherit; font-weight: 700; cursor: pointer; background: #2251d1; color: white; }
    button.secondary { background: #eef3ff; color: #2251d1; }
    .hidden { display: none !important; }
    .card { border: 1px solid #e1e8f5; border-radius: 16px; padding: 16px; background: #fbfcff; margin: 12px 0; }
    .toast { margin-top: 16px; padding: 12px 14px; border-radius: 12px; background: #eaf8ef; color: #176635; font-weight: 700; }
  </style>
</head>
<body>
  <main>
    <section data-testid="login-page">
      <h1 data-testid="login-title">Demo Shop</h1>
      <label>Email <input data-testid="login-username"></label>
      <label>Password <input data-testid="login-password" type="password"></label>
      <button data-testid="login-submit" type="button">Sign in</button>
      <div data-testid="login-success-toast" class="toast hidden">Signed in as admin@example.test</div>
    </section>

    <section data-testid="catalog-page" class="hidden">
      <h2>Catalog</h2>
      <p data-testid="catalog-result-count">1 item</p>
      <label>Search <input data-testid="catalog-search"></label>
      <article class="card">
        <h3>Migrator Mug</h3>
        <p>$12.00</p>
        <button data-testid="catalog-add-mug" type="button">Add mug</button>
      </article>
      <button data-testid="cart-open" class="secondary" type="button">Cart (<span data-testid="cart-count">0</span>)</button>
    </section>

    <section data-testid="cart-page" class="hidden">
      <h2>Cart</h2>
      <p>Total: <strong data-testid="cart-total">$0.00</strong></p>
      <button data-testid="checkout" type="button">Checkout</button>
    </section>

    <section data-testid="orders-page" class="hidden">
      <h2>Orders</h2>
      <p data-testid="orders-status">No orders yet</p>
    </section>
  </main>
  <script>
    const $ = id => document.querySelector(`[data-testid="${id}"]`);
    const switchTo = id => ['login-page', 'catalog-page', 'cart-page', 'orders-page'].forEach(x => $(x).classList.toggle('hidden', x !== id));
    $('login-submit').addEventListener('click', () => { $('login-success-toast').classList.remove('hidden'); setTimeout(() => switchTo('catalog-page'), 80); });
    $('catalog-search').addEventListener('input', () => $('catalog-result-count').textContent = '1 item');
    $('catalog-add-mug').addEventListener('click', () => $('cart-count').textContent = '1');
    $('cart-open').addEventListener('click', () => { $('cart-total').textContent = '$12.00'; switchTo('cart-page'); });
    $('checkout').addEventListener('click', () => { switchTo('orders-page'); $('orders-status').textContent = 'Order demo-1001 created'; });
  </script>
</body>
</html>
""";

    static string BuildPlaywrightProofCsproj() => """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
    <PackageReference Include="Microsoft.Playwright.NUnit" Version="1.52.0" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
</Project>
""";

    static string BuildPlaywrightProofTest() => """
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace PublicDemo.PlaywrightProof;

public class StaticAppSmokeTests : PageTest
{
    [Test]
    public async Task LoginCatalogCartAndOrdersFlow_UsesTheSameTestIdsAsGeneratedOutput()
    {
        await Page.GotoAsync(PublicDemoApp.Url);
        await Expect(Page.GetByTestId("login-title")).ToContainTextAsync("Demo Shop");
        await Page.GetByTestId("login-username").FillAsync("demo@example.test");
        await Page.GetByTestId("login-password").FillAsync("secret");
        await Page.GetByTestId("login-submit").ClickAsync();
        await Expect(Page.GetByTestId("login-success-toast")).ToBeVisibleAsync();
        await Page.GetByTestId("catalog-search").FillAsync("mug");
        await Expect(Page.GetByTestId("catalog-result-count")).ToHaveTextAsync("1 item");
        await Page.GetByTestId("catalog-add-mug").ClickAsync();
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("1");
        await Page.GetByTestId("cart-open").ClickAsync();
        await Expect(Page.GetByTestId("cart-total")).ToHaveTextAsync("$12.00");
        await Page.GetByTestId("checkout").ClickAsync();
        await Expect(Page.GetByTestId("orders-status")).ToContainTextAsync("Order demo-1001 created");
    }

    static class PublicDemoApp
    {
        public static string Url => new Uri(FindAppIndex()).AbsoluteUri;

        static string FindAppIndex()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "app", "index.html");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not find app/index.html from the test output directory.");
        }
    }
}
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
        driver.Navigate().GoToUrl("app/index.html");
        driver.FindElement(By.CssSelector("[data-testid='login-username']")).SendKeys("demo@example.test");
        driver.FindElement(By.CssSelector("[data-testid='login-password']")).SendKeys("secret");
        driver.FindElement(By.CssSelector("[data-testid='login-submit']")).Click();
        Assert.That(driver.FindElement(By.CssSelector("[data-testid='login-success-toast']")).Displayed, Is.True);
        driver.FindElement(By.CssSelector("[data-testid='catalog-search']")).SendKeys("mug");
        Assert.That(driver.FindElement(By.CssSelector("[data-testid='catalog-result-count']")).Text, Is.EqualTo("1 item"));
        driver.FindElement(By.CssSelector("[data-testid='catalog-add-mug']")).Click();
        Assert.That(driver.FindElement(By.CssSelector("[data-testid='cart-count']")).Text, Is.EqualTo("1"));
        driver.FindElement(By.CssSelector("[data-testid='cart-open']")).Click();
        Assert.That(driver.FindElement(By.CssSelector("[data-testid='cart-total']")).Text, Is.EqualTo("$12.00"));
        driver.FindElement(By.CssSelector("[data-testid='checkout']")).Click();
        Assert.That(driver.FindElement(By.CssSelector("[data-testid='orders-status']")).Text, Does.Contain("Order demo-1001 created"));
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
  "UiTargets": [
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='login-username']\"))", "TargetExpression": "GetByTestId(\"login-username\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='login-password']\"))", "TargetExpression": "GetByTestId(\"login-password\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='login-submit']\"))", "TargetExpression": "GetByTestId(\"login-submit\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='login-success-toast']\"))", "TargetExpression": "GetByTestId(\"login-success-toast\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='catalog-search']\"))", "TargetExpression": "GetByTestId(\"catalog-search\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='catalog-result-count']\"))", "TargetExpression": "GetByTestId(\"catalog-result-count\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='catalog-add-mug']\"))", "TargetExpression": "GetByTestId(\"catalog-add-mug\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='cart-count']\"))", "TargetExpression": "GetByTestId(\"cart-count\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='cart-open']\"))", "TargetExpression": "GetByTestId(\"cart-open\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='cart-total']\"))", "TargetExpression": "GetByTestId(\"cart-total\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='checkout']\"))", "TargetExpression": "GetByTestId(\"checkout\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" },
    { "SourceExpression": "driver.FindElement(By.CssSelector(\"[data-testid='orders-status']\"))", "TargetExpression": "GetByTestId(\"orders-status\")", "TargetKind": "TestId", "SourceTruth": "app/index.html" }
  ],
  "Methods": [],
  "PageObjects": []
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
using System.Threading.Tasks;

namespace PublicDemo.Playwright;

[Trait("migration", "public-playground")]
public class LoginSmokeFacts : PageTest, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UserCanSubmitLoginForm()
    {
        await Page.GotoAsync("app/index.html");
        await Page.GetByTestId("login-username").FillAsync("demo@example.test");
        await Page.GetByTestId("login-password").FillAsync("secret");
        await Page.GetByTestId("login-submit").ClickAsync();
        await Expect(Page.GetByTestId("login-success-toast")).ToBeVisibleAsync();
        await Page.GetByTestId("catalog-search").FillAsync("mug");
        await Expect(Page.GetByTestId("catalog-result-count")).ToHaveTextAsync("1 item");
        await Page.GetByTestId("catalog-add-mug").ClickAsync();
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("1");
        await Page.GetByTestId("cart-open").ClickAsync();
        await Expect(Page.GetByTestId("cart-total")).ToHaveTextAsync("$12.00");
        await Page.GetByTestId("checkout").ClickAsync();
        await Expect(Page.GetByTestId("orders-status")).ToContainTextAsync("Order demo-1001 created");
    }
}
""";
        }

        return """
// Generated by Migrator — Selenium C# → Playwright .NET
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Threading.Tasks;

namespace PublicDemo.Playwright;

[TestFixture]
public class LoginSmokeTestPlaywright : PageTest
{
    [Test]
    public async Task UserCanSubmitLoginForm()
    {
        await Page.GotoAsync("app/index.html");
        await Page.GetByTestId("login-username").FillAsync("demo@example.test");
        await Page.GetByTestId("login-password").FillAsync("secret");
        await Page.GetByTestId("login-submit").ClickAsync();
        await Expect(Page.GetByTestId("login-success-toast")).ToBeVisibleAsync();
        await Page.GetByTestId("catalog-search").FillAsync("mug");
        await Expect(Page.GetByTestId("catalog-result-count")).ToHaveTextAsync("1 item");
        await Page.GetByTestId("catalog-add-mug").ClickAsync();
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("1");
        await Page.GetByTestId("cart-open").ClickAsync();
        await Expect(Page.GetByTestId("cart-total")).ToHaveTextAsync("$12.00");
        await Page.GetByTestId("checkout").ClickAsync();
        await Expect(Page.GetByTestId("orders-status")).ToContainTextAsync("Order demo-1001 created");
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
- Static fake app: app/index.html
- Config-backed UI targets: 12
- TODOs: 0
- Unsupported actions: 0
- Runtime readiness score: 100

## Quality trend

The public playground represents the expected green path for a tiny pilot scope.

## Triage decisions

| Decision | Item | Reason |
|---|---|---|
| accept | public playground smoke | Selectors are explicit, generated output is reviewable, and the optional Playwright proof can exercise the fake app. |
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
    <p>What good looks like: one generated smoke test, a static fake app, zero unsupported actions, reviewable evidence.</p>
    <h2>Quality trend</h2>
    <p>Stable green sample for public demo use.</p>
    <h2>Runtime proof</h2>
    <p>Optional Playwright proof: <code>playwright-dotnet-proof/StaticAppSmokeTests.cs</code>.</p>
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
- [ ] Static app data-testid values back the generated Playwright selectors.
- [ ] Optional Playwright proof result is attached when browser runtime validation is needed.
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
