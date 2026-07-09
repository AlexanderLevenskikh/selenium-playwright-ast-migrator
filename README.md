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

## Public preview story: evidence before scale

`public-preview-flow/v1` is the recommended public-preview route: install, run `doctor install`, start with playground or product `start`, migrate through pilot/waves, stop on gates, extract mapping research from noisy waves, and share a safe `feedback-bundle/v1` instead of a private repository dump.

The safe-by-default rule is simple: generated output is a draft until verification, final gate, artifact hygiene, sentinel lifecycle, and wave quality evidence agree. When the run is red, follow `migration/current-ticket.md` and the [Wave mode operator runbook](docs/wave-mode-operator-runbook.md) instead of starting another wave. For the compact end-to-end route, see [Public preview flow](docs/public-preview-flow.md).


## Choose your path

Harness run lifecycle is owned by `new-harness-run.ps1`; agents use the installed Harness Kit scripts instead of inventing migration/runs folders.


### Product-repo onboarding wizard

If you are inside a real product repository and do not want to choose the workflow by hand, start here:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
```

`start` detects the source, creates `migration/profiles/adapter-config.start.json`, writes `migration/next-commands.md`, `migration/current-ticket.md`, and `migration/state/start-dispatch.json`, then points you to install diagnostics, agent bootstrap, `pilot`, `doctor`, and the dashboard after a run exists. Use `--agent codex`, `--agent generic`, or `--agent manual` to choose the handoff route.

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

`bootstrap-opencode` now also copies the project command pack into the repository root (`opencode.jsonc`, `.opencode/agents`, `.opencode/commands`, and `AGENTS.md` when missing). Then open the repository in OpenCode and run:

```text
/supervised-task waves
```

The `waves` mode is the recommended divide-and-conquer start: it uses the `kit bootstrap-opencode --source ...` source as the hard scope when configured, auto-detects only missing source/target/framework details, asks only for missing required inputs, runs kit doctor, creates the wavefront plan, materializes the first wave, and runs only the wave-local migration. It must not run a full-source migration or discover sibling functional-test projects before a wave workspace exists. All `migration/**` artifacts are repository-root state; nested workspaces such as `Web/**/migration/**` are treated as process defects.

For an existing workspace, plain `/supervised-task` resumes the next bounded action. After a successful FINAL/PASS checkpoint, the supervised agent stops once for review and reports evidence. To continue into post-final research without writing a long prompt, run `/supervised-task continue` or plain `/supervised-task` after `FINAL_STOPPED_FOR_REVIEW`. For day-to-day operations after a wave is already running, use the [Wave mode operator runbook](docs/wave-mode-operator-runbook.md): it explains `BLOCKED_BY_GATE`, `current-ticket.md`, sentinel finding lifecycle, wave quality budget, mapping research memory, and feedback bundle handoff.

### 3. Migrate with another agent

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
# or:
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

This writes `migration/AGENT_HANDOFF.md`, `migration/AGENT_CONTRACT.md`, and the kickoff prompts without pretending the workflow is OpenCode-specific.

Before scaling a real migration, let the CLI choose a small representative pilot slice:

```bash
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

`pilot` writes `pilot-selection.md/json`, `selected-tests.txt`, `next-commands.md`, and a copied `selected-input/` directory. The generated next commands analyze/migrate `selected-input/`, not the full suite. The selection tries to cover simple smoke tests, PageObjects, table/filter patterns, waits, assertions, custom helpers, XPath, and data-driven tests.

After any real run, open the dashboard first:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Open `migration/dashboard/latest/report-dashboard.html` before digging through raw JSON/TXT artifacts. When TODOs remain, `explain-todo` also writes `suggested-config-patch.md/json` with grouped root causes, “fix this profile mapping first”, confidence/evidence badges, and draft UiTarget/Method/Table entries for review.

### Share a safe feedback bundle

If the migrator produces many TODOs, syntax fallbacks, unresolved symbols, or a `verify-project` failure, you can help improve the tool without sending your private repository. From the product repo root, run:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

or on macOS/Linux/WSL:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

The script writes a `feedback-bundle/v1` zip under `migration/state/feedback-bundles/`. It includes reports/evidence such as mapping research memory, wave quality budget, sentinel findings, `project-verify-report.*`, `project-verify-harness.csproj`, `migration-board.*`, and `explain-todo.md`. It excludes project source and generated `.cs` samples by default. Review `manifest.json` before sharing the zip.

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

### Cross-platform lifecycle scripts

The CLI itself does not require PowerShell when installed through npm or standalone. The migration-kit lifecycle scripts are different: every repository `.ps1` script has a same-name `.sh` companion, and thin Unix wrappers delegate to PowerShell 7 (`pwsh`) so Windows and Unix run the same implementation. On macOS/Linux/WSL, install PowerShell 7 before using `migration/scripts/*.sh` wrappers or release/package shell entrypoints: https://learn.microsoft.com/powershell/scripting/install/installing-powershell. `selenium-pw-migrator kit doctor` reports this as the `powershell-7` check.

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

For a real project, start with onboarding and a representative pilot slice:

```bash
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

Then bootstrap the guarded workspace once and let the agent own the run lifecycle:

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

For an agent-assisted migration, do not hand-create `migration/` folders or `migration/runs/<run-id>/`. Start from the product onboarding state, run the representative pilot, then choose the matching agent handoff.

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

OpenCode path:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

Codex/generic/CI path:

```shell
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

OpenCode install modes:

```text
--project-desktop / --opencode-install project-desktop  Windows OpenCode Desktop
--opencode-install project-local                        macOS/Linux/WSL OpenCode CLI
--opencode-install ci                                   Legacy compatibility; prefer bootstrap-agent for non-OpenCode agents
```

Then start the selected agent environment and run `/supervised-task`, or give a non-OpenCode agent `migration/AGENT_HANDOFF.md` and `migration/AGENT_CONTRACT.md`. The orchestrator must read `migration/current-ticket.md`, `migration/state/start-dispatch.json`, and `migration/pilot/next-commands.md`; it should not ask the user for a broad menu when the state is clear.

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
| `memory` | Stable | Manage project-scoped migration memory (`init/add/explain/doctor/summarize/recall`) under `migration/state/memory/**` for supervised runs. |
| `migration` | Stable | Build divide-and-conquer wave plans (`inventory/cluster/plan/plan show`) and prepare bounded wave run workspaces (`run-wave`) with project-scoped memory deltas. |
| `config merge-deltas` / `config validate-merge` | Stable | Merge wave-local `config-delta.json` files into a reviewable candidate config and validate conflicts before promotion. |
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

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.


## Divide-and-conquer wave planning

For larger projects, prefer the OpenCode one-command wavefront start. Install/update the guarded project command pack once, then let `/supervised-task waves` run the setup chain:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Open the repository in OpenCode and run:

```text
/supervised-task waves
```

That command should auto-detect source/target/framework when possible, run doctor, create the plan, materialize the first wave, and run only the wave-local migration. Manual commands remain available for debugging/CI:

```bash
selenium-pw-migrator migration plan --input ./SeleniumTests --strategy wavefront --workspace migration --out migration/plan
selenium-pw-migrator migration plan show --plan migration/plan
```

The planner writes `inventory.json`, `clusters.json`, `waves.json`, `plan.md`, `selected-tests.txt`, `memory-recall.md`, and `next-commands.md`. It does not migrate files. When `kit bootstrap-opencode --source ...` has configured a source, that source is the hard wavefront boundary; sibling functional-test projects must not be discovered, planned, copied, or suggested. The first wave contains representative tests, later waves expand by cluster. `run-wave` passes `--selected-tests selected-tests.txt` to the migrate pipeline, so execution remains test-selected instead of migrating every test in each copied file. Agents should run `memory explain`, `memory doctor`, and `memory recall --file` before turning a wave into a bounded task.

Prepare a bounded wave run workspace manually only when you are debugging the agent setup or running CI:

```bash
selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001
```

`migration run-wave` materializes `source-scope/`, `generated/`, `input-scope.json`, `config-delta.json`, `memory-delta.jsonl`, `run-summary.md`, `wave-status.json`, and migrate scripts. It is project-scoped only: it does not promote memory, does not merge config, and does not publish cross-project/org knowledge packs. Use `--execute-migrate true` only when you want the command to invoke the existing `--mode migrate` pipeline for the wave scope immediately.

Merge reviewed wave-local config deltas into a candidate config only after the wave has evidence:

```bash
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

`config merge-deltas` writes `adapter-config.merged.json`, `merge-report.md/json`, and `conflicts.jsonl`. `config validate-merge` writes `validate-merge-report.md/json`. Neither command promotes the candidate automatically; Reviewer, Watchdog, and Final Gate must accept the merge, and `conflicts.jsonl` must be empty.


### Wavefront / memory / config-merge snapshot

After using project-scoped memory, wavefront planning, `migration run-wave`, or `config merge-deltas`, open the normal dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

The dashboard includes a **Wavefront / memory / config-merge snapshot** with project-scoped memory counts, wave progress, next wave candidates, config-merge status, and suggested next commands. The generated `report-dashboard-evidence.zip` also carries nearby `state/memory`, `plan`, and `config-merge` artifacts as review evidence.


### Agent orchestration rails

Migrator Kit writes `migration/state/scope-contract.json` during kit bootstrap/init so supervised waves know the allowed source root, workspace root, forbidden roots, and command classes. The final gate reads this contract and fails out-of-scope changes. File-based claim scripts under `migration/scripts/*claim*` provide a lightweight lease/heartbeat MVP for parallel wave agents. See `docs/agent-orchestration.md`.
