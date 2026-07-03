using System.Text.Json;

namespace Migrator.Core;

public sealed class ScaffoldWriter
{
    private readonly ScaffoldOptions _options;

    public ScaffoldWriter(ScaffoldOptions options)
    {
        _options = options;
    }

    public ScaffoldResult Write()
    {
        var outPath = _options.OutPath;
        var targetTestFramework = NormalizeTargetTestFramework(_options.TargetTestFramework);
        if (targetTestFramework == null)
        {
            return new ScaffoldResult(
                Status: "failed",
                OutputPath: outPath,
                CreatedFiles: Array.Empty<string>(),
                SkippedFiles: Array.Empty<string>(),
                Warnings: new[] { $"Unsupported target test framework '{_options.TargetTestFramework}'. Supported values: nunit, xunit." },
                NextSteps: Array.Empty<string>());
        }

        if (Directory.Exists(outPath) && Directory.EnumerateFileSystemEntries(outPath).Any())
        {
            return new ScaffoldResult(
                Status: "failed",
                OutputPath: outPath,
                CreatedFiles: Array.Empty<string>(),
                SkippedFiles: Array.Empty<string>(),
                Warnings: new[] { $"Output directory '{outPath}' already exists and is not empty. Use a different path or remove the existing directory." },
                NextSteps: Array.Empty<string>());
        }

        Directory.CreateDirectory(outPath);
        var createdFiles = new List<string>();
        var warnings = new List<string>();
        var ns = _options.Namespace;
        var projectName = _options.ProjectName;

        WriteCsproj(outPath, projectName, targetTestFramework, createdFiles);
        WriteTestBase(outPath, ns, targetTestFramework, createdFiles);
        WriteTestSettings(outPath, ns, createdFiles);
        WriteSmokeTest(outPath, ns, targetTestFramework, createdFiles);
        WriteDraftConfig(outPath, ns, projectName, targetTestFramework, createdFiles);
        WriteScaffoldReadme(outPath, projectName, targetTestFramework, createdFiles, warnings);
        WriteGitignore(outPath, createdFiles);

        var nextSteps = new[]
        {
            $"Review GeneratedTestBase.cs and implement LoginAsync for your project ({DisplayFrameworkName(targetTestFramework)} scaffold).",
            "Set the E2E_BASE_URL environment variable to your test environment URL.",
            "Replace TestSettings.DefaultRoute with a real route from your application.",
            "Review and fill in adapter-config.draft.json with source-truth selectors.",
            $"Run migrate with this config: --mode migrate --target dotnet --target-test-framework {targetTestFramework} --input <selenium> --config adapter-config.draft.json --out src",
            "Run verify on generated code: --mode verify --input src --config adapter-config.draft.json --out verify",
            "Install Playwright browsers: pwsh bin/Debug/net10.0/playwright.ps1 install",
            "Run compile smoke: dotnet build",
            "Run tests (after configuring auth/routes): dotnet test"
        };

        return new ScaffoldResult(
            Status: "completed",
            OutputPath: outPath,
            CreatedFiles: createdFiles.ToArray(),
            SkippedFiles: Array.Empty<string>(),
            Warnings: warnings.ToArray(),
            NextSteps: nextSteps);
    }

    static string? NormalizeTargetTestFramework(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "nunit";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "nunit" or "n-unit" => "nunit",
            "xunit" or "x-unit" => "xunit",
            _ => null
        };
    }

    static string DisplayFrameworkName(string targetTestFramework) =>
        targetTestFramework.Equals("xunit", StringComparison.OrdinalIgnoreCase) ? "xUnit" : "NUnit";

    void WriteCsproj(string dir, string projectName, string targetTestFramework, List<string> created)
    {
        var path = Path.Combine(dir, $"{projectName}.csproj");
        var packageReferences = targetTestFramework == "xunit"
            ? @"    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.12.0"" />
    <PackageReference Include=""Microsoft.Playwright.Xunit"" Version=""1.52.0"" />
    <PackageReference Include=""xunit"" Version=""2.9.2"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.8.2"" />
    <PackageReference Include=""Microsoft.Playwright"" Version=""1.52.0"" />"
            : @"    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.12.0"" />
    <PackageReference Include=""Microsoft.Playwright.NUnit"" Version=""1.52.0"" />
    <PackageReference Include=""NUnit"" Version=""4.2.2"" />
    <PackageReference Include=""NUnit3TestAdapter"" Version=""4.6.0"" />
    <PackageReference Include=""Microsoft.Playwright"" Version=""1.52.0"" />";

        var content = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
{packageReferences}
  </ItemGroup>

</Project>
";
        File.WriteAllText(path, content);
        created.Add($"{projectName}.csproj");
    }

    void WriteTestBase(string dir, string ns, string targetTestFramework, List<string> created)
    {
        var path = Path.Combine(dir, "GeneratedTestBase.cs");
        var frameworkUsing = targetTestFramework == "xunit"
            ? "using Microsoft.Playwright.Extensions.Xunit;"
            : "using Microsoft.Playwright.NUnit;\nusing NUnit.Framework;";

        var content = $@"using Microsoft.Playwright;
{frameworkUsing}

namespace {ns};

/// <summary>
/// Base class for generated Playwright tests.
/// Implement LoginAsync and configure routes for your project.
/// This is a compile-oriented starter kit — it does NOT guarantee runtime pass
/// until you configure auth, routes, and test data.
/// </summary>
public abstract class GeneratedTestBase : PageTest
{{
    protected string BaseUrl => TestSettings.BaseUrl;

    protected async Task GoToAsync(string route)
    {{
        var url = route.StartsWith(""http"", StringComparison.OrdinalIgnoreCase)
            ? route
            : $""{{BaseUrl.TrimEnd('/')}}/{{route.TrimStart('/')}}"";

        await Page.GotoAsync(url);
    }}

    protected async Task LoginAsync()
    {{
        // TODO: Add project-specific authentication.
        // Example:
        // await GoToAsync(TestSettings.LoginRoute);
        // await Page.GetByTestId(""login-input"").FillAsync(""test-user"");
        // await Page.GetByTestId(""password-input"").FillAsync(""test-password"");
        // await Page.GetByTestId(""login-button"").ClickAsync();
        // await WaitForAppReadyAsync();
        await Task.CompletedTask;
    }}

    protected async Task WaitForAppReadyAsync()
    {{
        // TODO: Add project-specific wait strategy if needed.
        // Example: await Page.GetByTestId(""app-ready"").WaitForAsync(State.Visible);
        await Task.CompletedTask;
    }}
}}
";
        File.WriteAllText(path, content);
        created.Add("GeneratedTestBase.cs");
    }

    void WriteTestSettings(string dir, string ns, List<string> created)
    {
        var path = Path.Combine(dir, "TestSettings.cs");
        var content = $@"namespace {ns};

/// <summary>
/// Test environment settings. Override via environment variables.
/// All default values are placeholders — replace with your project's values.
/// </summary>
public static class TestSettings
{{
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable(""E2E_BASE_URL"")
        ?? ""https://example.test"";

    public static string LoginRoute =>
        Environment.GetEnvironmentVariable(""E2E_LOGIN_ROUTE"")
        ?? ""/<test-login>"";

    public static string DefaultRoute =>
        Environment.GetEnvironmentVariable(""E2E_DEFAULT_ROUTE"")
        ?? ""/<ROUTE_SOURCE_TRUTH_REQUIRED>"";
}}
";
        File.WriteAllText(path, content);
        created.Add("TestSettings.cs");
    }

    void WriteSmokeTest(string dir, string ns, string targetTestFramework, List<string> created)
    {
        var path = Path.Combine(dir, "ExampleSmokeTest.cs");
        var frameworkUsing = targetTestFramework == "xunit" ? "using Xunit;" : "using NUnit.Framework;";
        var classAttributes = targetTestFramework == "xunit"
            ? string.Empty
            : @"[TestFixture]
[Parallelizable(ParallelScope.Self)]
";
        var methodAttribute = targetTestFramework == "xunit" ? "[Fact]" : "[Test]";

        var content = $@"using Microsoft.Playwright;
{frameworkUsing}

namespace {ns};

/// <summary>
/// Example smoke test showing the generated test style.
/// This is NOT a real test — it will only pass once you configure
/// BaseUrl, auth, routes, and test data for your project.
/// </summary>
{classAttributes}public class ExampleSmokeTest : GeneratedTestBase
{{
    {methodAttribute}
    public async Task Smoke()
    {{
        await LoginAsync();
        await GoToAsync(TestSettings.DefaultRoute);
        await WaitForAppReadyAsync();

        // TODO: Replace with a real assertion from your application.
        await Assertions.Expect(Page.Locator(""body"")).ToBeVisibleAsync();
    }}
}}
";
        File.WriteAllText(path, content);
        created.Add("ExampleSmokeTest.cs");
    }

    void WriteDraftConfig(string dir, string ns, string projectName, string targetTestFramework, List<string> created)
    {
        var path = Path.Combine(dir, "adapter-config.draft.json");
        var config = new
        {
            GeneratedBy = "Migrator scaffold",
            RequiresReview = true,
            SourceProjectName = "<SOURCE_TRUTH_REQUIRED>",
            LocatorSettings = new
            {
                DefaultTestIdAttribute = "data-testid",
                KnownTestIdAttributes = new[] { "data-testid", "data-test-id", "data-test", "data-tid" }
            },
            TestHost = targetTestFramework == "xunit"
                ? new
                {
                    TargetTestFramework = targetTestFramework,
                    Namespace = ns,
                    BaseClass = "GeneratedTestBase",
                    ClassAttributes = Array.Empty<string>(),
                    Usings = new[] { "Microsoft.Playwright", "Microsoft.Playwright.Extensions.Xunit", "Xunit" },
                    SetUpStatements = new[]
                    {
                        "await LoginAsync();",
                        "await GoToAsync(TestSettings.DefaultRoute);",
                        "await WaitForAppReadyAsync();"
                    }
                }
                : new
                {
                    TargetTestFramework = targetTestFramework,
                    Namespace = ns,
                    BaseClass = "GeneratedTestBase",
                    ClassAttributes = new[] { "TestFixture", "Parallelizable(ParallelScope.Self)" },
                    Usings = new[] { "NUnit.Framework", "Microsoft.Playwright" },
                    SetUpStatements = new[]
                    {
                        "await LoginAsync();",
                        "await GoToAsync(TestSettings.DefaultRoute);",
                        "await WaitForAppReadyAsync();"
                    }
                },
            UiTargets = Array.Empty<object>(),
            PageObjects = Array.Empty<object>(),
            Methods = Array.Empty<object>(),
            Verification = new
            {
                AutoDiscoverNearestProject = false,
                PackageReferences = targetTestFramework == "xunit"
                    ? new[]
                    {
                        new { Include = "Microsoft.Playwright.Xunit", Version = "1.52.0" },
                        new { Include = "xunit", Version = "2.9.2" },
                        new { Include = "xunit.runner.visualstudio", Version = "2.8.2" }
                    }
                    : new[]
                    {
                        new { Include = "Microsoft.Playwright.NUnit", Version = "1.52.0" },
                        new { Include = "NUnit", Version = "4.2.2" },
                        new { Include = "NUnit3TestAdapter", Version = "4.6.0" }
                    }
            },
            QualityGates = new
            {
                FailOnPlaceholderLeftovers = true,
                FailOnUnmappedTargets = false,
                FailOnUnsupportedActions = false
            }
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        created.Add("adapter-config.draft.json");
    }

    void WriteScaffoldReadme(string dir, string projectName, string targetTestFramework, List<string> created, List<string> warnings)
    {
        var path = Path.Combine(dir, "README.md");
        var frameworkName = DisplayFrameworkName(targetTestFramework);
        var content = $@"# {projectName} — Scaffolded Playwright .NET Project

This is a **compile-oriented starter kit** generated by the Migrator.
It is **NOT** a runtime-ready test project.

## What this scaffold is

- A minimal Playwright .NET + {frameworkName} test project structure
- A base class (`GeneratedTestBase`) with navigation and login hooks
- Environment-variable-based configuration (`TestSettings`)
- A draft adapter config for the Migrator (`adapter-config.draft.json`)
- An example smoke test showing the expected test style

## What this scaffold is NOT

- Not a working test suite — no runtime pass is claimed
- Not a replacement for project-specific infrastructure
- Not a complete migration — you still need to configure auth, routes, test data, and selectors

## Setup steps

### 1. Install Playwright browsers

```bash
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install
```

### 2. Configure environment variables

Set these before running tests:

```bash
# Windows PowerShell
$env:E2E_BASE_URL=""https://your-test-env.example.com""
$env:E2E_LOGIN_ROUTE=""<test-login>""
$env:E2E_DEFAULT_ROUTE=""<ROUTE_SOURCE_TRUTH_REQUIRED>""
```

### 3. Implement LoginAsync

Edit `GeneratedTestBase.cs` and replace the TODO in `LoginAsync` with your project's authentication flow.

### 4. Replace TestSettings placeholders

Edit `TestSettings.cs` and replace `<test-login>` and `<ROUTE_SOURCE_TRUTH_REQUIRED>` with real values.

### 5. Review adapter-config.draft.json

This draft config has `RequiresReview: true`. Fill in:
- `SourceProjectName` — your Selenium project name
- `UiTargets` — source-truth selector mappings
- `PageObjects` — page object declarations

### 6. Run migration

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode migrate --target dotnet --target-test-framework {targetTestFramework} --input ""<selenium-tests>"" --config ""adapter-config.draft.json"" --out ""src"" --format both
```

### 7. Verify and iterate

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode verify --input ""src"" --config ""adapter-config.draft.json"" --out ""verify"" --format both
```

### 8. Compile smoke

Copy generated files into this project and run:

```bash
dotnet build
```

### 9. Run tests (after full configuration)

```bash
dotnet test
```

## Important

**No runtime pass is guaranteed.** This scaffold provides the infrastructure skeleton. Runtime tests require:
- Configured `E2E_BASE_URL` pointing to a real test environment
- Working authentication flow
- Valid test data
- Project-specific route configuration
- Source-truth selector mappings in `adapter-config.draft.json`

## Next steps

See the [Migrator documentation](https://github.com/<repo>/tree/main/docs) for:
- Framework matrix
- Quick start guide
- Profile cookbook (UiTargets, Methods, Scopes)
- Migration workflow
- Agent playbooks
";
        File.WriteAllText(path, content);
        created.Add("README.md");
    }

    void WriteGitignore(string dir, List<string> created)
    {
        var path = Path.Combine(dir, ".gitignore");
        var content = @"bin/
obj/
TestResults/
playwright-report/
*.trx
.env
*.local.json
";
        File.WriteAllText(path, content);
        created.Add(".gitignore");
    }
}

public sealed class ScaffoldResult
{
    public string Status { get; init; } = null!;
    public string OutputPath { get; init; } = null!;
    public string[] CreatedFiles { get; init; } = null!;
    public string[] SkippedFiles { get; init; } = null!;
    public string[] Warnings { get; init; } = null!;
    public string[] NextSteps { get; init; } = null!;

    public ScaffoldResult(string Status, string OutputPath, string[] CreatedFiles, string[] SkippedFiles, string[] Warnings, string[] NextSteps)
    {
        this.Status = Status;
        this.OutputPath = OutputPath;
        this.CreatedFiles = CreatedFiles;
        this.SkippedFiles = SkippedFiles;
        this.Warnings = Warnings;
        this.NextSteps = NextSteps;
    }
}
