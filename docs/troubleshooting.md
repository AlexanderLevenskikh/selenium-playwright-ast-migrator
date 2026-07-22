# Troubleshooting

> **Execution model:** one standard full-project run is supported. `pilot` is optional calibration; partition-specific planning and acceptance state are not used.

## Installation diagnostics starts with PATH, not dotnet tool list

Do not start diagnostics with `dotnet tool list` only. The CLI can be installed as standalone, npm wrapper, dotnet global tool, or dotnet local tool. First check what the current shell actually resolves:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
selenium-pw-migrator --version
```

On Linux/macOS/WSL:

```bash
command -v selenium-pw-migrator
which -a selenium-pw-migrator || true
selenium-pw-migrator --version
```

Then inspect package managers:

```powershell
dotnet tool list --global
dotnet tool list --local
npm list -g selenium-pw-migrator --depth=0
npm config get registry
```

For a fuller report, run `./scripts/diagnose-install.ps1` or `scripts/diagnose-install.sh`. See [install diagnostics](install-diagnostics.md).

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

## I installed standalone, but PowerShell still runs the dotnet tool

PowerShell resolves `selenium-pw-migrator` from the first matching directory in `PATH`.
Check every visible installation:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
selenium-pw-migrator --version
```

For standalone to win on Windows, this path should appear before `%USERPROFILE%\.dotnet\tools`:

```text
%USERPROFILE%\.selenium-pw-migrator\bin
```

The standalone Windows installer adds that directory to the user `PATH` by default, moves it to the front even when it was already present later, and also prepends it to the current PowerShell session. Open a new terminal if another session still sees the old command. If `%USERPROFILE%\.dotnet\tools` still wins, reinstall with `-RemoveDotnetTool` or remove the old global tool manually:

```powershell
dotnet tool uninstall --global SeleniumPlaywrightMigrator
```

To bypass `PATH` entirely, run the installed executable directly:

```powershell
& "$env:USERPROFILE\.selenium-pw-migrator\bin\selenium-pw-migrator.exe" --version
```

On Linux/macOS/WSL, check command priority with:

```bash
which -a selenium-pw-migrator
selenium-pw-migrator --version
```



## `bootstrap-opencode` says `unchanged`, but `/supervised-task` is still an old command

Check both managed copies:

```powershell
Select-String -Path migration/opencode-team/global/.config/opencode/commands/supervised-task.md -Pattern "selenium-pw-migrator run"
Select-String -Path .opencode/commands/supervised-task.md -Pattern "selenium-pw-migrator run"
```

Current builds treat `migration/opencode-team/**` as kit-owned during update. A normal bootstrap must refresh that workspace command pack first and then resync the repository-root `.opencode` directories:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source <selenium-source> `
  --opencode-install none
```

Expected update markers include `kit-overwrite: ...migration/opencode-team/...` and `sync: .../.opencode/commands`. Older builds had a defect where the workspace copy was preserved and the stale file was copied back to `.opencode`; for that older version only, `--force` is a one-time workaround.

## Standard-run recovery

After an interruption, inspect the latest `migration/runs/run-*` directory, current reports, `current-ticket.md`, and git diff. Resume from concrete artifacts. Never recreate missing verification JSON or copy a PASS receipt from another run. Apply one bounded fix, then repeat the complete configured source scope.

## `artifact-hygiene` reports a PowerShell syntax error, but `validate-scripts.ps1` passed

The repository validator checks Migrator source scripts and templates. An existing product workspace can still contain an older or locally modified copy under `migration/scripts/`.

Update the installed kit-owned scripts first:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source <selenium-source> `
  --opencode-install none
```

Then validate the installed workspace directly:

```powershell
pwsh ./migration/scripts/validate-installed-scripts.ps1 -Workspace migration -RequireShell
```

When running from the Migrator source repository, the source validator can include an external workspace:

```powershell
pwsh ./scripts/validate-scripts.ps1 -Root . -Workspace <path-to-product-repo>/migration -RequireShell
```

A source-only `SCRIPT_VALIDATE_PASS` is not evidence that an older generated workspace copy is valid. New final gates run `installed-script-syntax` before `artifact-hygiene` when the workspace validator is installed.
