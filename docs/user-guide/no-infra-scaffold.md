# No-Infra Scaffold

Generate a minimal, compile-ready Playwright .NET test project when your team has no existing Playwright infrastructure.

## When to use scaffold

Use `--mode scaffold` when:

- Your team has Selenium C# tests but no Playwright .NET project
- You need a starting point for migration without reverse-engineering someone else's infrastructure
- You want a consistent, Migrator-aware project structure

## When NOT to use scaffold

Do not use `--mode scaffold` when:

- You already have a Playwright .NET project with tests, base classes, and auth flow → use `discover-target` instead
- You need a runtime-ready test suite → scaffold is compile-only, not runtime-ready
- You want the tool to configure auth, routes, or test data → you must do this manually

## discover-target vs scaffold

| Aspect | `discover-target` | `scaffold` |
|---|---|---|
| **Prerequisite** | Existing Playwright .NET project | None |
| **Input** | `--input` pointing to target project | Not required |
| **Output** | `target-inventory.json`, `adapter-config.draft.json` | Full project: `.csproj`, base class, config, smoke test |
| **Best for** | Teams with existing Playwright infra | Teams starting from scratch |
| **Runtime-ready** | Depends on existing infra | No — compile-only starter |

## Generated files

`scaffold` creates the following files in the output directory:

| File | Purpose |
|---|---|
| `*.csproj` | .NET 10 test project with Playwright + NUnit or xUnit packages |
| `GeneratedTestBase.cs` | Abstract base class with `LoginAsync`, `GoToAsync`, `WaitForAppReadyAsync` |
| `TestSettings.cs` | Environment-variable-based configuration (`E2E_BASE_URL`, `E2E_LOGIN_ROUTE`, `E2E_DEFAULT_ROUTE`) |
| `ExampleSmokeTest.cs` | Example test showing the expected style |
| `adapter-config.draft.json` | Draft adapter config with `RequiresReview: true` |
| `README.md` | Setup guide for the scaffolded project |
| `.gitignore` | Standard ignore rules for .NET test projects |

Additionally, report files are generated in the output directory:
- `scaffold-report.json` — structured report
- `scaffold-report.md` — human-readable report

## How to run

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode scaffold --out "./new-playwright-tests"
```

Optional flags:
- `--target-test-framework nunit|xunit` — selects the generated Playwright .NET test framework (default: `nunit`)
- `--format text|json|both` — controls which report files are generated (default: `both`)

The output directory must not exist or be empty. If it exists and contains files, the scaffold fails safely without modifying anything.

## What you must fill in manually

The scaffold is a skeleton. You must:

### 1. Implement authentication

Edit `GeneratedTestBase.cs` and replace the `LoginAsync` stub with your project's authentication flow.

### 2. Configure environment variables

Set environment variables before running tests:

```bash
$env:E2E_BASE_URL="https://your-test-env.example.com"
$env:E2E_LOGIN_ROUTE="/login"
$env:E2E_DEFAULT_ROUTE="/dashboard"
```

### 3. Replace placeholder routes

Edit `TestSettings.cs` and replace `<test-login>` and `<ROUTE_SOURCE_TRUTH_REQUIRED>` with real values from your application.

### 4. Review and fill adapter-config.draft.json

The draft config has `RequiresReview: true`. Fill in:
- `SourceProjectName` — your Selenium project name
- `UiTargets` — source-truth selector mappings
- `PageObjects` — page object declarations

### 5. Install Playwright browsers

```bash
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install
```

## Why runtime pass is not guaranteed

The scaffold provides infrastructure skeleton only. Runtime tests require:

- A real test environment (`E2E_BASE_URL`)
- Working authentication flow
- Valid test data
- Project-specific route configuration
- Source-truth selector mappings in `adapter-config.draft.json`

The scaffold does not and cannot provide any of these.

## Using adapter-config.draft.json after review

Once you've reviewed and filled in the draft config:

1. Copy or rename it to `adapter-config.json`
2. Run migration:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode migrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./generated" --format both
```

3. Run verify:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode verify --input "./generated" --config "./adapter-config.json" --out "./verify" --format both
```

4. Copy generated files into your scaffolded project and run compile smoke:

```bash
dotnet build
```

## Two paths summary

```
Path A: Have Playwright infra
  discover-target → review config → orchestrate

Path B: No Playwright infra
  scaffold → implement auth/routes → review config → migrate → verify
```

See [Migration Workflow](migration-workflow.md) for the full process.
