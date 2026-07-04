# Quick start

This path gets you from a small Selenium sample to generated Playwright output. Start with 1-5 tests before scaling to a full suite.

## Install the CLI

Recommended standalone install, no .NET SDK/runtime required to run the CLI:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
```

For a project-pinned .NET tool, see [Tool installation](tool-installation.md).

## Short happy path

```bash
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

Use this playground first. For real migrations, keep the production promise focused on Selenium C# -> Playwright .NET; Java, Python, and Playwright TypeScript remain experimental preview paths.

## Prerequisites

- .NET 10 SDK
- Selenium test files to migrate
- Optional but recommended: `adapter-config.json` with verified PageObject/helper mappings


## Agent-assisted guarded start

For a guarded OpenCode/Codex-style run, prefer the Migration Kit path over manually creating folders. See [Agent environments](agent-environments.md) for Windows Desktop, macOS/Linux/WSL CLI, Codex, CI, and other agents. From the product repository root, use the one-command bootstrap when you want the workspace, OpenCode team templates, `kit doctor`, and an environment-specific agent setup in one step:

```bash
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

Use `--opencode-install project-local` for macOS/Linux/WSL OpenCode CLI and `--opencode-install ci` for Codex/CI/manual agents.

Manual fallback:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

Windows OpenCode Desktop fallback:

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

After that, open the selected agent environment and run `/supervised-task`, or give the kickoff prompt to a non-OpenCode agent. The agent should create or resume the active harness run itself through `new-harness-run.sh` from bash or `new-harness-run.ps1` from PowerShell; it should not ask you to manually create `migration/runs/<run-id>/`.

Use the manual steps below when you are running the CLI yourself without an agent.

## 1. Check the tool and your input

For a new migration, start with the onboarding wizard:

```bash
dotnet tool run selenium-pw-migrator -- init --wizard --source ./SeleniumTests --target dotnet --target-test-framework nunit --workspace migration
```

For an existing config/workspace, run the preflight checks directly:

```bash
dotnet tool run selenium-pw-migrator -- --help
dotnet tool run selenium-pw-migrator -- --mode doctor --input ./SeleniumTests --config ./adapter-config.json --out doctor
```

Relative `--out` values are written under the default `migration/` workspace. The command above writes to `migration/doctor`.

## 2. Analyze Selenium tests

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out analysis \
  --format both
```

Important outputs:

- `migration/analysis/report.md` / `report.json`
- `migration/analysis/unmapped-targets.json`
- `migration/analysis/unsupported-actions.json`
- `migration/analysis/migration-quality-dashboard.md`
- `migration/analysis/migration-quality-tickets.md`

## 3. Add or improve source-truth mappings

Use the report to fill in `adapter-config.json`. Do not guess selectors. Use Selenium PageObject code, verified HTML attributes, existing Playwright tests/POMs, or helper semantics that your project owns.

Small example:

```json
{
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "submit-button",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [],
  "Methods": []
}
```

## 4. Generate Playwright output

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out generated-tests \
  --format both
```

Generated files and migration reports are written to `migration/generated-tests`.

## 5. Verify generated output

For renderer-level checks:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out verify \
  --format both
```

For project-aware Playwright .NET compile checks:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-project \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out verify-project \
  --format both
```

For TypeScript preview output, generate with `--target ts` and type-check with `verify-ts-project --ts-project <path>`.

## 6. Run the full dry-run workflow

After the first pass works, use orchestration:

```bash
dotnet tool run selenium-pw-migrator -- --mode orchestrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out run-001 \
  --format both
```

Typical output:

```text
migration/run-001/
  analyze/
  generated/
  verify/
  propose/
  orchestration-report.md
  orchestration-report.json
```

## Next steps

- [End-to-end simple example](examples/end-to-end-simple.md)
- [Migration workflow](user-guide/migration-workflow.md)
- [Config and profile guide](config-profile-guide.md)
- [Limitations](user-guide/limitations.md)


Windows OpenCode Desktop shortcut: `--project-desktop` remains an alias for `--opencode-install project-desktop`.
