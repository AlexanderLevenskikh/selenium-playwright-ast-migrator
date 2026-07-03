# Troubleshooting

## `--help` works, but my command fails immediately

Run command-specific help first:

```bash
selenium-pw-migrator --mode migrate --help
```

Check whether the mode requires `--input`, `--config`, `--before`, `--after`, `--ts-project`, or another mode-specific option.

## Output is not where I expected

Relative `--out` values are written under the default `migration/` workspace:

```bash
selenium-pw-migrator --mode analyze --input ./SeleniumTests --out analysis
# writes migration/analysis
```

Use `--workspace <dir>` to change the workspace root, or pass an absolute `--out` path.

## Generated locators contain `TODO:`

This means the profile did not contain a trusted mapping for that source expression. Use:

```bash
selenium-pw-migrator --mode index-pom --input ./SeleniumTests --out pom-index --format both
selenium-pw-migrator --mode helper-inventory --input ./SeleniumTests --out helper-inventory --format both
```

Then add mappings only from verified source truth: Selenium POM selectors, existing Playwright POM/tests, or actual HTML attributes.

## `verify-project` fails to compile

Common causes:

- Missing target project references or NuGet packages.
- Generated `TestHost` settings do not match your Playwright project.
- Mapped helper statements are written for a different target backend.
- Unsupported actions remain in setup or class-level code.

Start with the first compile error. Fix one config or renderer issue, then rerun `verify-project`.

## TypeScript output fails type-checking

The TypeScript target is experimental. Use target-specific profile overrides when default `.NET` mapped statements are not TS-safe:

```json
{
  "SourceMethod": "WaitVisible",
  "TargetStatements": ["await Assertions.Expect({TARGET}).ToBeVisibleAsync();"],
  "Targets": {
    "playwright-typescript": {
      "TargetStatements": ["await expect({TARGET}).toBeVisible();"]
    }
  }
}
```

Then run:

```bash
selenium-pw-migrator --mode verify-ts-project --input migration/generated-ts --ts-project ./playwright-ts --out verify-ts-project
```

## `dotnet tool install` cannot find the package

When testing locally, pack first and point installation to the local package source:

Windows PowerShell:

```powershell
.\scripts\pack-tool.ps1 -Version 0.0.0-preview.1
.\scripts\install-local-tool.ps1 -Version 0.0.0-preview.1
```

macOS/Linux/WSL:

```bash
scripts/pack-tool.sh 0.0.0-preview.1
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator --version 0.0.0-preview.1 --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

CI package smoke uses the same idea: pack → verify `.nupkg` contents → install from local source → run `--help` and `doctor`.

## `dotnet tool update` says `--ignore-failed-sources` is a version

This usually means the shell variable passed to `--version` is empty. PowerShell then treats the next token as the version value:

```powershell
dotnet tool update SeleniumPlaywrightMigrator --local --version $version --ignore-failed-sources
```

Check the variable first:

```powershell
$version
```

If it prints nothing, set it and pack the same version:

```powershell
$version = "0.0.0-preview.local.$(Get-Date -Format yyyyMMddHHmmss)"
dotnet pack .\Migrator.Cli\Migrator.Cli.csproj -c Release -o .\artifacts\local-tool /p:Version=$version
dotnet tool update SeleniumPlaywrightMigrator --local --add-source .\artifacts\local-tool --version $version --ignore-failed-sources
```

## The agent keeps asking whether to continue

Use `docs/guarded-opencode-desktop-runbook.ru.md` as the launch procedure. In guarded mode, routine continuation decisions should be made by the agent inside `migration/**`. The agent should stop only for a classified blocker, scope violation, loop/plateau, or failed final gate evidence.

## Runtime Playwright tests fail after generated code compiles

Compilation does not prove runtime correctness. Classify runtime failures into:

- wrong/missing locator;
- missing auth or environment setup;
- missing test data;
- helper semantics not captured by config;
- table/list strategy mismatch;
- product-state wait missing;
- generated code bug.

Use `smoke-plan` and `runtime-classify` to prioritize follow-up work from generated artifacts and runtime logs.
