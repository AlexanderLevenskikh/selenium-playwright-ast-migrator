# Selenium → Playwright AST Migrator

[![npm preview](https://img.shields.io/npm/v/selenium-pw-migrator/preview?label=npm%20preview)](https://www.npmjs.com/package/selenium-pw-migrator)
[![NuGet preview](https://img.shields.io/nuget/vpre/SeleniumPlaywrightMigrator?label=NuGet)](https://www.nuget.org/packages/SeleniumPlaywrightMigrator)

**A .NET 10 CLI toolkit for turning Selenium test suites into measurable, reviewable Playwright migrations.**

The Migrator parses Selenium tests, builds an intermediate representation, applies project-specific profile mappings, and renders Playwright tests plus reports. It is designed for teams that want to migrate large E2E suites without pretending that every selector, helper, wait, and PageObject can be guessed safely.

The main production path is **Selenium C# → Playwright .NET** with **NUnit as the default target framework** and **xUnit as a supported target framework**. Other source/target combinations are available as preview features and are clearly labeled below.

## What it does

- Analyzes Selenium tests and reports unmapped targets, unsupported actions, and repeated migration patterns.
- Maps PageObjects, helper methods, table/list patterns, waits, and project conventions through reviewable JSON profiles.
- Generates Playwright .NET tests, or experimental Playwright TypeScript specs when a TS target is selected.
- Verifies generated output with syntax checks, project-aware compile checks, TypeScript type checks, quality gates, migration dashboards, and a migration-quality backlog with root cause / next-action tickets.
- Helps humans or coding agents iterate safely: source truth → profile/config → generated code → verification → next pattern.

The goal is not magic conversion. The goal is to make migration uncertainty visible and fixable.


## Choose your path

### 1. Try it without an agent

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

Open `playground/try-this-first.md` and run the generated commands. This is the safest disposable route.

### 2. Migrate with OpenCode

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Then run `/supervised-task` in OpenCode. The harness creates or resumes `migration/runs/<run-id>`; do not create run folders by hand.

### 3. Migrate with another agent

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
# or:
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

This writes `migration/AGENT_HANDOFF.md`, `migration/AGENT_CONTRACT.md`, and the kickoff prompts without pretending the workflow is OpenCode-specific.

After any real run, open the dashboard first:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Open `migration/dashboard/latest/report-dashboard.html` before digging through raw JSON/TXT artifacts.

## Supported sources and targets

| Source frontend | Target backend | Status | Notes |
|---|---|---|---|
| Selenium C# / NUnit or xUnit | Playwright .NET / NUnit or xUnit | Stable public path | Full analyze/migrate/verify workflow with Roslyn-based recognition; NUnit remains the default target framework. |
| Selenium C# / NUnit | Playwright TypeScript | Experimental preview | Use `--target ts`; project-aware verification requires `--ts-project`. |
| Selenium Java | Playwright .NET / TypeScript | Experimental MVP | Useful for simple Java Selenium fixtures; no Java semantic model. |
| Selenium Python | Playwright .NET / TypeScript | Experimental spike | Useful for simple pytest/unittest Selenium diagnostics; not production-ready. |

## Install

### Frontend-friendly option: npm wrapper

The npm package is the default public path for frontend/test-automation teams. It is a thin wrapper over the same standalone release archives, so users do not need to install the .NET SDK.

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
```

Update:

```bash
npm update -g selenium-pw-migrator
# or print the detected channel-specific command:
selenium-pw-migrator self update
```

`doctor install` (mode-compatible form: `--mode install-doctor`) shows the resolved executable, version, channel, runtime, PATH candidates, and recommended install/update command. This is the first command to run when global npm, standalone, dotnet tool, or local tool installs may be shadowing each other. Use it to diagnose what your shell actually runs before checking package-manager state.

### Recommended: standalone CLI

For locked-down environments or release smoke tests, the standalone distribution is still the most direct install path. The npm wrapper remains the default frontend-friendly route above, but standalone does not require the .NET SDK or .NET Runtime on the target machine. Use it when npm is not available or when you want a direct GitHub Release install.

Windows PowerShell:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
```

Linux/macOS/WSL:

```bash
curl -fsSL https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.sh -o /tmp/install-standalone.sh
bash /tmp/install-standalone.sh
export PATH="$HOME/.selenium-pw-migrator/bin:$PATH"
selenium-pw-migrator --version
```

The Windows installer adds the standalone directory to the front of the user `PATH` by default, even if it was already present later. For troubleshooting install priority, use `Get-Command selenium-pw-migrator -All` on Windows or `which -a selenium-pw-migrator` on Unix-like shells. To remove an older dotnet global tool in the same install step, pass `-RemoveDotnetTool`.

To uninstall the standalone Windows install, run the same installer with `-Uninstall`. On Linux/macOS, run `install-standalone.sh --uninstall` and remove the PATH line from your shell profile.

### npm details

For a pinned preview, install a specific npm version or use the matching GitHub Release asset:

```bash
npm install -g selenium-pw-migrator@0.0.0-preview.8
npm install -g https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8.tgz
```

The npm `postinstall` downloads the matching standalone archive for `win-x64`, `linux-x64`, `osx-x64`, or `osx-arm64`, verifies `checksums.sha256` when available, and preserves the native CLI exit code. Corporate installs can use a Nexus npm proxy plus `--selenium-pw-migrator-base-url` for an internal standalone archive mirror. Isolated registry smoke scripts are available for npmjs and Nexus installs. See [npm wrapper](docs/npm-wrapper.md). Publishing instructions live in [npm publishing](docs/npm-publishing.md).

### .NET developers: dotnet tool

Use the dotnet tool distribution when you want a global/local .NET tool or a project-pinned `.config/dotnet-tools.json`. This path requires the .NET SDK.

```bash
dotnet tool install --global SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease
selenium-pw-migrator --help
```

Clone the repository only if you want to contribute or build the tool from source.

## Build or run locally from source

From source:

```bash
dotnet restore
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --help
```

As a locally built dotnet tool package, use commands for your shell.

Windows PowerShell:

```powershell
.\scripts\pack-tool.ps1 -Version 0.0.0-preview.1
.\scripts\install-local-tool.ps1 -Version 0.0.0-preview.1
dotnet tool run selenium-pw-migrator -- --help
```

macOS/Linux/WSL:

```bash
scripts/pack-tool.sh 0.0.0-preview.1
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator --version 0.0.0-preview.1 --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

Use `selenium-pw-migrator --help` only after a global install. For repository-local tool manifests, prefer `dotnet tool run selenium-pw-migrator -- ...`.

See [Tool installation](docs/tool-installation.md), [Standalone installation](docs/standalone-installation.md), [npm wrapper](docs/npm-wrapper.md), and [Packaging and distribution](docs/packaging-and-distribution.md).

## Happy path

For the stable production path, keep it boring and small:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

For a real project, bootstrap the guarded workspace once and then let the agent own the run lifecycle:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

For Codex or another agent, use the explicit non-OpenCode handoff:

```bash
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

Then run `/supervised-task` in OpenCode, or hand `migration/AGENT_HANDOFF.md`, `migration/AGENT_CONTRACT.md`, and `migration/prompts/kickoff-prompt.txt` to another agent. Do not create `migration/runs/<run-id>` manually; the harness does that.

Java, Python, and Playwright TypeScript paths are experimental. Keep release demos and production migration promises focused on Selenium C# -> Playwright .NET.

## Quick start

Start with a small pilot directory, not the whole suite:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out doctor

dotnet tool run selenium-pw-migrator -- --mode orchestrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out run-001 \
  --format both
```

By default, relative `--out` values are written under the `migration/` workspace, for example `migration/run-001`.

Typical outputs:

```text
migration/run-001/
  analyze/
  generated/
  verify/
  propose/
  orchestration-report.md
  orchestration-report.json
```


Try the five-minute playground:

```bash
dotnet tool run selenium-pw-migrator -- playground --out playground --target-test-framework xunit --generation-policy conservative
dotnet tool run selenium-pw-migrator -- playground verify --input playground --out playground-verify
cat playground/try-this-first.md
```

For a file-by-file walkthrough, see:

- [Quick start](docs/quick-start.md)
- [Init wizard](docs/init-wizard.md)
- [Migration runbook](docs/migration-runbook.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md)
- [End-to-end simple example](docs/examples/end-to-end-simple.md)
- [Public demo and guided tutorial](docs/public-demo-tutorial.md)
- [Public Demo / Playground](docs/public-playground.md)
- [Teaching demo: AST migration explained](examples/teaching-demo/README.md)
- [AST migration explained](docs/articles/ast-migration-explained.md) / [RU](docs/articles/ast-migration-explained.ru.md)
- [Public demo files](examples/public-demo/README.md)
- [Migration workflow](docs/user-guide/migration-workflow.md)
- [Extensibility and public API](docs/extensibility.md)


## Guarded agent quick start

For an agent-assisted migration, do not hand-create `migration/` folders. Install the kit and OpenCode team templates, then let the supervised agent create or resume the harness run.

First-time product-repo bootstrap, including the migration workspace, OpenCode team templates, `kit doctor`, and an environment-specific OpenCode setup:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

Use explicit install modes when needed:

```text
--project-desktop / --opencode-install project-desktop  Windows OpenCode Desktop
--opencode-install project-local                        macOS/Linux/WSL OpenCode CLI
--opencode-install ci                                   Codex/CI/manual agents; no OpenCode config
```

Manual fallback when you do not want to install an OpenCode config in the same command:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

Then start the selected agent environment and run `/supervised-task`, or give a non-OpenCode agent `migration/prompts/kickoff-prompt.txt`. The orchestrator must create or resume `migration/runs/<run-id>/` through `new-harness-run.ps1`, read `Prompt.md` / `Plan.md` / `Implement.md` / `Documentation.md`, record events, run `check-harness-policy.ps1`, and only claim final success after the final gate passes.

The only manual bootstrap that remains is installing/updating the tool and project-local OpenCode config. Once those are present, the agent should manage the workspace lifecycle and run artifacts itself.

See [Migrator Agent Harness Kit](docs/migrator-agent-harness-kit.md), [Agent environments](docs/agent-environments.md), [Harness dashboard](docs/migrator-agent-harness-dashboard.md), and the canonical [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md).

Developer smoke for the bootstrap template-root resolver:

```powershell
pwsh .\scripts\run-kitroot-shadow-smoke.ps1 -Clean
```

This creates a fake product repository that contains its own `templates/migration-kit` folder and verifies that `bootstrap-opencode` still uses the bundled Migrator templates.

## Main CLI modes

| Mode | Status | Purpose |
|---|---|---|
| `runbook` | Stable | Generate a practical migration plan with pilot scope, command chain, risk map, artifacts, and acceptance checklist. |
| `playground` | Stable | Create a five-minute public demo workspace with ready commands, expected outputs, dashboard sample, and PR pack sample. |
| `playground-verify` | Stable | Verify that the generated playground still has the manifest, command chain, demo input, expected output, and safety wording. |
| `doctor` | Stable | Preflight checks plus safe `--fix` repair plans for inputs, config layers, project files, and workspace hygiene. |
| `release-doctor` | Stable | Check NuGet preview readiness: package metadata, docs, scripts, workflow dry-run, secret references, and release hygiene. |
| `analyze` | Stable | Parse Selenium files and produce reports without generating target files. |
| `migrate` | Stable | Generate Playwright target files. |
| `verify` | Stable | Run lightweight generated-code verification. |
| `verify-project` | Stable | Compile generated Playwright .NET tests against a real project-aware harness. |
| `config-validate` | Stable | Validate profile structure and safety rules. |
| `config-diff` | Stable | Compare profile changes and highlight risky edits. |
| `guard` | Stable | Compare before/after migration metrics and catch regressions. |
| `index-pom` | Stable | Mine Selenium PageObjects plus target-side Playwright/Kontur POM selector evidence. |
| `selector-evidence` | Experimental | Explain Selenium selector → config mapping → generated locator provenance with confidence and unsafe/inferred flags. |
| `agent-contract` | Experimental | Generate a ticket-specific agent contract pack with allowed paths, stop policy, exact commands, and coordinator/migrator/verifier prompts. |
| `pr-pack` | Experimental | Create a PR/review bundle with PR summary, changed/generated files list, before/after metrics, risk summary, reviewer checklist, evidence references, and suggested PR description. |
| `learn-pack` | Experimental | Extract reusable migration knowledge from completed runs into a reviewable profile layer and learning changelog. |
| `config-author` | Experimental | Generate evidence-driven config proposals and a reviewable patch without applying it. |
| `helper-inventory` | Stable | Inspect helper/POM method bodies and infer MethodSemantics candidates. |
| `discover-target` | Stable | Scan an existing Playwright .NET project and create a reviewable target inventory. |
| `scaffold` | Stable | Generate a minimal compile-ready Playwright .NET project scaffold. |
| `bootstrap-project` | Stable | Create reusable migration profile skeletons for a new source project. |
| `capabilities` | Stable | List built-in source frontend / target backend capability reports. |
| `verify-ts-project` | Experimental | Type-check generated Playwright TS specs inside an existing TS project. |
| `orchestrate` | Experimental | Run analyze → migrate → verify → propose as one dry-run workflow. |
| `explain-todo` / `smoke-plan` / `runtime-classify` / `selector-evidence` / `migration-board` / `report-serve` | Experimental | Prioritize follow-up work from migration artifacts/runtime logs, classify runtime root causes, score readiness, explain selector provenance, and export triage decisions. |
| `evidence pack` | Stable | Create a redacted shareable zip with reports, generated artifacts, manifest, and checksums. |
| `profile list/search/inspect/install/diff` | Experimental | Use offline built-in profiles as reviewed config layers. |

Run command-specific help with:

```bash
selenium-pw-migrator --mode migrate --help
```

## Safety rules

- Never invent selectors.
- Prefer source truth: Selenium PageObject code, verified HTML attributes, existing target POM/tests, or project-owned helper semantics.
- Treat generated TODO comments as reviewable evidence, not as failure to hide.
- Do not manually patch generated files as the final fix; improve the profile, source-truth mapping, or migrator behavior.
- Use `index-pom` and `helper-inventory` before suppressing or manually rewriting repeated PageObject/helper patterns.

If Selenium POMs contain proven selectors such as `ByTId("value")`, `CreateControlByTid(...)`, explicit `data-tid`, CSS, XPath, or resolved constants, prefer this order: existing target POM member → generated POM scaffold → raw Playwright locator from proven selector → explicit TODO.

## Documentation map

- [Complete user guide](USER_GUIDE.md)
- [Полное руководство пользователя](USER_GUIDE.ru.md)
- [Documentation index](docs/README.md)
- [Quick start](docs/quick-start.md)
- [Init wizard](docs/init-wizard.md)
- [Migration runbook](docs/migration-runbook.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md)
- [Teaching demo: AST migration explained](examples/teaching-demo/README.md)
- [AST migration explained](docs/articles/ast-migration-explained.md) / [RU](docs/articles/ast-migration-explained.ru.md)
- [Framework matrix](docs/framework-matrix.md) — static support table plus `framework matrix` generated readiness reports
- [Doctor fix mode](docs/doctor-fix-mode.md)
- [Report serve dashboard](docs/report-serve-dashboard.md)
- [Profile marketplace](docs/profile-marketplace.md)
- [Migration PR pack](docs/migration-pr-pack.md)
- [Migration learning pack](docs/migration-learning-pack.md)
- [Config Authoring Assistant](docs/config-authoring-assistant.md)
- [Generation Policy](docs/generation-policy.md)
- [Evidence pack workflow](docs/evidence-pack.md)
- [User guide](docs/user-guide/README.md)
- [Config and profile guide](docs/config-profile-guide.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md) — canonical guarded agent launch procedure
- [Limitations](docs/user-guide/limitations.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Migration quality program](docs/migration-quality-program.md)
- [Public roadmap](docs/public-roadmap.md)
- [Release process](docs/release-process.md)

## Development

```bash
dotnet restore
dotnet test --no-restore
```

The test suite covers parser behavior, adapter mappings, snapshots, compile-smoke checks, orchestration, TypeScript target basics, safety guards, packaging guardrails, and regression cases for common migration blockers.

## Public release status

This project is currently prepared as a public preview. Stable commands are intended for external users; experimental commands may change between preview releases. See [CHANGELOG.md](CHANGELOG.md), [SECURITY.md](SECURITY.md), and [CONTRIBUTING.md](CONTRIBUTING.md).
