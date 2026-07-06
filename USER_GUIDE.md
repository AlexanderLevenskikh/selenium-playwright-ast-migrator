# Migrator User Guide

Migrator is a command-line toolkit for moving Selenium end-to-end tests to Playwright in a controlled, reviewable way.

It does not try to pretend that a whole test suite can be converted perfectly in one click. Instead, it gives you a repeatable loop:

```text
inspect the old tests -> collect source truth -> generate Playwright code -> verify -> improve the profile -> repeat
```

The main production path is Selenium C# to Playwright .NET. NUnit is the default target test framework, and xUnit is also supported. Playwright TypeScript, Java Selenium input, and Python Selenium input exist as preview paths.

## Happy path

Harness run lifecycle is owned by `new-harness-run.ps1`; agents use the installed Harness Kit scripts instead of inventing migration/runs folders.


Install the CLI first. The recommended public path is the npm wrapper, which downloads the matching standalone CLI and does not require the .NET SDK:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

For a real product repository, start with the onboarding wizard and representative pilot slice:

```bash
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

For a real agent-assisted migration:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
# Windows OpenCode Desktop legacy shortcut:
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --project-desktop
# non-OpenCode handoff:
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

After a run, open the dashboard first:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Open `migration/dashboard/latest/report-dashboard.html` before digging through raw artifacts. When TODOs remain, run `explain-todo`; it now writes `suggested-config-patch.md/json` with grouped root causes, confidence/evidence badges, and draft profile mappings for review.

The stable production path is Selenium C# -> Playwright .NET. Treat Java, Python, and Playwright TypeScript as experimental preview paths until their reports and target-project checks prove readiness.

## 1. The Big Picture

Migrator works best when you treat it as a migration assistant, not a blind text converter.

The tool reads Selenium tests and project conventions, builds an intermediate model of test actions, applies a reviewable JSON profile, renders Playwright tests, and produces reports that explain what is ready, what is uncertain, and what needs more evidence.

The important idea is source truth. Migrator should only generate confident code when the old project or the target project proves what should happen. Good source truth includes:

- Selenium PageObject selectors.
- Existing Playwright PageObjects or test base classes.
- Project helper methods with known behavior.
- Real attributes such as `data-testid`, `data-tid`, CSS selectors, XPath selectors, or resolved selector constants.
- Explicit adapter config reviewed by a person.

Weak guesses become TODOs or reports instead of unsafe generated code.

## 2. Installation And Local Use

### Fast npm install

Use npm when you want the least surprising public install/update path:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
npm update -g selenium-pw-migrator
```

`selenium-pw-migrator self update` prints the channel-specific update command without mutating global installs automatically. Mode-compatible diagnostics form: `--mode install-doctor`.

### Fast standalone install

Use standalone when npm is unavailable and you still do not want to install the .NET SDK or .NET Runtime:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
selenium-pw-migrator doctor install
```

### Fast start from NuGet

Use the dotnet tool when you want a global/local .NET tool or a project-pinned `.config/dotnet-tools.json`. You do not need to clone the repository to use the migrator:

```bash
dotnet tool install --global SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease
selenium-pw-migrator --help
```

For team repositories, use a project-local dotnet tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease
dotnet tool run selenium-pw-migrator -- --help
```

Then create the disposable demo playground:

```bash
dotnet tool run selenium-pw-migrator -- playground \
  --out playground \
  --target-test-framework xunit \
  --generation-policy conservative
```

Open `playground/try-this-first.md` and run the generated command chain before touching a real project.

Relative `--out playground` is resolved from the current directory. The generated command scripts keep run artifacts under the selected playground folder, so nonstandard `--out` paths remain self-contained.

If you want to understand the AST-based migration model before using the tool on a real suite, open the teaching demo and article:

- `examples/teaching-demo/README.md`
- `docs/articles/ast-migration-explained.md`
- `docs/articles/ast-migration-explained.ru.md`

### Install from a locally packed package

If you are validating a release candidate before publishing it:

```bash
./scripts/pack-tool.sh 0.0.0-preview.1
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator \
  --version 0.0.0-preview.1 \
  --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

On Windows, use `./scripts/pack-tool.ps1 -Version 0.0.0-preview.1`.

### Run from source

From the repository:

```bash
dotnet restore
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --help
```

Most examples below assume a local dotnet tool manifest and use `dotnet tool run selenium-pw-migrator -- ...`. Use `selenium-pw-migrator ...` only after a global install. When running from source, replace the local-tool prefix with:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- 
```

For example:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode analyze --input ./OldTests --out analysis
```

## 3. Recommended First Run

Start with the playground or a representative pilot slice. Do not begin with the entire suite.

```shell
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

For a real product repository, prefer `start` over the older manual wizard:

```shell
selenium-pw-migrator start --input ./OldTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot
```

`start` creates the product onboarding state:

- `migration/current-ticket.md` - the active bounded migration scope.
- `migration/next-commands.md` - exact next commands for the chosen route.
- `migration/profiles/adapter-config.start.json` - starter profile skeleton.
- `migration/state/start-dispatch.json` - no-menu dispatch state for `/supervised-task`.

`pilot` creates a bounded representative input:

- `migration/pilot/pilot-selection.md/json` - why files were selected.
- `migration/pilot/selected-tests.txt` - selected source files.
- `migration/pilot/selected-input/` - copied pilot input.
- `migration/pilot/next-commands.md` - analyze/migrate commands that point at `selected-input`, not the full suite.

For OpenCode, install the agent team after `start`:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

Then run `/supervised-task`. After a successful FINAL/PASS checkpoint, `/supervised-task` stops for review by default. Use `/supervised-task continue` to start post-final TODO/source-truth research without writing a detailed supervisor prompt. The supervised agent should read `current-ticket.md` and `state/start-dispatch.json`, create or resume `migration/runs/<run-id>/`, and avoid asking the user broad menu questions when the state is clear.

For Codex, CI, or another agent, use the explicit handoff path:

```shell
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json
```

`bootstrap-opencode --opencode-install ci` remains supported as a legacy compatibility mode, but new non-OpenCode setups should use `bootstrap-agent`.

If you are working without an agent and only want the older starter config/scaffold, `init --wizard` is still available as the manual scaffold path:

```shell
selenium-pw-migrator init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --workspace migration
```

## 4. The Normal Migration Loop

For most projects, use this sequence.

### Step 1. Check the input and config

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out doctor \
  --format both
```

Use `doctor --fix` to create safe repair plans and candidate files:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --dry-run \
  --out doctor-fix
```

Use `--apply` only when you are comfortable with the proposed safe workspace/config candidate writes:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --apply \
  --out doctor-fix
```

### Step 2. Collect source truth

Run this before large PageObject or helper mapping work:

```bash
dotnet tool run selenium-pw-migrator -- --mode index-pom \
  --input ./OldTests \
  --out pom-index \
  --format both
```

Then inspect helper wrappers:

```bash
dotnet tool run selenium-pw-migrator -- --mode helper-inventory \
  --input ./OldTests \
  --out helper-inventory \
  --format both
```

If you already have a Playwright .NET project, inspect it too:

```bash
dotnet tool run selenium-pw-migrator -- --mode discover-target \
  --input ./PlaywrightTests \
  --out target-discovery \
  --format both
```

### Step 3. Analyze without generating code

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out run-001-analysis \
  --format both
```

Read the report. It tells you what Migrator understood, what it could not map, and which unsupported patterns appear often.

### Step 4. Generate Playwright tests

For NUnit:

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out run-001-generated \
  --format both
```

For xUnit:

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework xunit \
  --out run-001-generated \
  --format both
```

### Step 5. Verify generated output

Use the lightweight verifier first:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify \
  --input migration/run-001-generated \
  --config migration/profiles/adapter-config.json \
  --out run-001-verify \
  --format both
```

For Playwright .NET, run project-aware verification:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-project \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target-test-framework nunit \
  --out run-001-verify-project \
  --format both
```

For Playwright TypeScript:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-ts-project \
  --input migration/run-001-generated \
  --ts-project ./PlaywrightTsProject \
  --out run-001-verify-ts \
  --format both
```

### Step 6. Explain what remains

```bash
dotnet tool run selenium-pw-migrator -- --mode explain-todo \
  --input migration/run-001-verify-project \
  --out run-001-explain \
  --format both
```

Then use the highest-impact categories to improve the profile or Migrator behavior.

### Step 7. Compare before and after

```bash
dotnet tool run selenium-pw-migrator -- --mode guard \
  --before migration/baseline \
  --after migration/run-001-verify-project \
  --out run-001-guard \
  --format both
```

Guard is useful in CI and agent loops because it catches regressions in TODOs, unsupported actions, syntax errors, and other migration metrics.

## 5. One-Command Orchestration

When the basic setup is ready, `orchestrate` runs the common dry-run workflow:

```bash
dotnet tool run selenium-pw-migrator -- --mode orchestrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out run-002 \
  --format both
```

It writes stage artifacts under the output folder, usually including analysis, generated files, verification, proposals, and an orchestration report.

Use orchestration for repeated migration runs. Use individual modes when you need to isolate one problem.

## 6. Test Framework Support

For C# Selenium input, Migrator can detect common NUnit and xUnit source tests.

For Playwright .NET output, Migrator supports:

- `--target-test-framework nunit`
- `--target-test-framework xunit`

NUnit is the default when no framework is specified.

Use xUnit when the target project is xUnit-based:

```bash
dotnet tool run selenium-pw-migrator -- --mode scaffold \
  --target-test-framework xunit \
  --out generated-scaffold
```

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
  --input ./OldTests \
  --config ./adapter-config.json \
  --target dotnet \
  --target-test-framework xunit \
  --out generated-xunit
```

The generated xUnit shape uses xUnit attributes and Playwright xUnit packages. NUnit output uses NUnit attributes and Playwright NUnit packages.

For Java, Python, and TypeScript target paths, framework support is still preview-level and should be validated with the generated reports and target project checks.

## 7. All CLI Modes In Plain Language

### Setup and discovery

`playground`

Creates a disposable five-minute demo workspace with ready commands, expected outputs, dashboard sample, PR pack sample, and manifest.

```bash
dotnet tool run selenium-pw-migrator -- playground --out playground --target-test-framework xunit --generation-policy conservative
```

`playground-verify`

Checks that a generated playground still contains the public demo contract: manifest, ready command chain, sample Selenium input, adapter config, expected Playwright output, dashboard sample, PR pack sample, and selector-safety wording.

```bash
dotnet tool run selenium-pw-migrator -- playground verify --input playground --out playground-verify
```

`memory`

Creates and validates project-scoped migration memory under `migration/state/memory/**`. Use it during supervised runs so later bounded actions can reuse decisions, warnings, final-gate lessons, and selector evidence without relying on chat memory.

```bash
dotnet tool run selenium-pw-migrator -- memory init --workspace migration
dotnet tool run selenium-pw-migrator -- memory add --kind decision "Keep POM unresolved until target mapping exists"
dotnet tool run selenium-pw-migrator -- memory explain --workspace migration
dotnet tool run selenium-pw-migrator -- memory doctor --workspace migration
```


`config-merge`

Merges reviewed wave-local `config-delta.json` files into a candidate config and validates the result before any promotion. This is the safe bridge between divide-and-conquer wave runs and the main `adapter-config.json`.

```bash
dotnet tool run selenium-pw-migrator -- config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
dotnet tool run selenium-pw-migrator -- config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

The command writes `adapter-config.merged.json`, `merge-report.md/json`, `validate-merge-report.md/json`, and `conflicts.jsonl`. The candidate is not promoted automatically; Reviewer, Watchdog, and Final Gate must accept the merge and `conflicts.jsonl` must be empty.

`release-doctor`

Checks NuGet preview readiness from the repository root: package metadata, version/changelog consistency, release scripts, README_TOOL packaging docs, publish workflow dry-run support, NuGet secret references, and repository hygiene.

```bash
dotnet tool run selenium-pw-migrator -- doctor release --out release-doctor
```

`runbook`

Generates a practical migration plan: pilot scope, command chain, risk map, artifacts to collect, and acceptance checklist.

```bash
dotnet tool run selenium-pw-migrator -- runbook --input ./OldTests --target dotnet --target-test-framework xunit --generation-policy conservative --out runbook
```

`start`

Product-repo onboarding wizard. It creates a profile skeleton, `current-ticket.md`, `next-commands.md`, and `state/start-dispatch.json` for no-menu `/supervised-task` dispatch.

```shell
selenium-pw-migrator start --input ./OldTests --agent opencode --workspace migration
```

`pilot`

Selects a representative slice, copies it into `selected-input/`, and writes next commands for the bounded input.

```shell
selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot
```

`init`

Legacy/manual scaffold wizard. Use it when you want the older starter config/scaffold without the product `start` state.

```shell
selenium-pw-migrator init --wizard --source-path ./OldTests --target dotnet --target-test-framework xunit
```

`doctor`

Checks whether the input, config, environment, and workspace are ready. With `--fix`, writes safe repair plans or candidate files.

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor --input ./OldTests --config ./adapter-config.json --fix --dry-run --out doctor
```

`capabilities`

Lists available source frontends and target backends.

```bash
dotnet tool run selenium-pw-migrator -- --mode capabilities --out capabilities --format both
```

`framework matrix`

Writes source framework detection and target framework readiness reports.

```bash
dotnet tool run selenium-pw-migrator -- framework matrix --input ./OldTests --target dotnet --target-test-framework xunit --out framework-matrix --format both
```

`discover-target`

Scans an existing Playwright .NET project and describes its namespaces, base classes, packages, and reusable infrastructure.

```bash
dotnet tool run selenium-pw-migrator -- --mode discover-target --input ./PlaywrightTests --out target-discovery
```

`scaffold`

Creates a minimal Playwright .NET test project skeleton.

```bash
dotnet tool run selenium-pw-migrator -- --mode scaffold --target-test-framework nunit --out scaffold
```

`bootstrap-project`

Creates reusable migration profile skeletons for a new project.

```bash
dotnet tool run selenium-pw-migrator -- --mode bootstrap-project --input ./OldTests --out bootstrap-oldtests
```

### Analysis and generation

`analyze`

Parses Selenium tests and reports what can be migrated.

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze --input ./OldTests --config ./adapter-config.json --out analysis
```

`migrate`

Generates Playwright output.

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate --input ./OldTests --config ./adapter-config.json --target dotnet --generation-policy balanced --out generated
```

Use `--generation-policy conservative|balanced|aggressive` to control mapped-helper risk. Conservative emits more review/TODO output, balanced is the normal default, and aggressive emits more active helper code with risk annotations.

`dump-ir`

Maintainer/debug command that dumps internal representation.

```bash
dotnet tool run selenium-pw-migrator -- --mode dump-ir --input ./OldTests --config ./adapter-config.json --out ir --ir-version both
```

`orchestrate`

Runs analyze, migrate, verify, and proposal generation as one dry-run flow.

```bash
dotnet tool run selenium-pw-migrator -- --mode orchestrate --input ./OldTests --config ./adapter-config.json --out run-001
```

### Verification and quality gates

`verify`

Checks generated code and reports syntax/TODO/config issues.

```bash
dotnet tool run selenium-pw-migrator -- --mode verify --input migration/generated --config ./adapter-config.json --out verify
```

`verify-project`

Builds generated Playwright .NET output in a temporary project-aware harness.

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-project --input ./OldTests --config ./adapter-config.json --out verify-project
```

`verify-ts-project`

Type-checks generated Playwright TypeScript specs inside an existing TS project.

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-ts-project --input migration/generated-ts --ts-project ./PlaywrightTs --out verify-ts
```

`guard`

Compares two runs and fails on regressions.

```bash
dotnet tool run selenium-pw-migrator -- --mode guard --before migration/baseline --after migration/current --out guard
```

### Config and profile work

`config-schema`

Writes the JSON Schema for adapter config.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-schema --out schema
```

`config-validate`

Validates adapter config structure and safety rules.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-validate --config ./adapter-config.json --validation-mode strict --out config-check
```

`config-normalize`

Maintainer command that converts older config shape into the newer profile shape.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-normalize --config ./adapter-config.json --out normalized
```

`config-diff`

Compares two configs and highlights risky changes.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff
```

`profile list`

Lists built-in offline profiles.

```bash
dotnet tool run selenium-pw-migrator -- profile list
```

`profile search`

Finds profiles by framework, backend, or capability.

```bash
dotnet tool run selenium-pw-migrator -- profile search xunit
```

`profile recommend`

Scores built-in profiles against a source project and recommends install order.

```bash
dotnet tool run selenium-pw-migrator -- profile recommend --input ./OldTests --target-test-framework xunit --out profile-recommendations
```

`profile inspect`

Explains a built-in profile before installation.

```bash
dotnet tool run selenium-pw-migrator -- profile inspect basic-csharp-xunit
```

`profile install`

Installs a built-in profile as a config layer.

```bash
dotnet tool run selenium-pw-migrator -- profile install basic-csharp-nunit --out profiles
```

`profile diff`

Compares a config to another config or built-in profile.

```bash
dotnet tool run selenium-pw-migrator -- profile diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff
```

`profile-match`

Estimates whether existing config/profile layers fit a source project.

```bash
dotnet tool run selenium-pw-migrator -- --mode profile-match --input ./OldTests --config ./profiles/base.adapter.json --out profile-match
```

`config author`

Writes evidence-driven config proposals and a reviewable patch from selector evidence, POM index, helper inventory, target discovery, and TODO reports. It does not apply the patch.

```bash
dotnet tool run selenium-pw-migrator -- config author --input migration/run-001 --config ./adapter-config.json --out config-proposals --format both
```

### Source truth helpers

`index-pom`

Finds Selenium PageObject selectors/source-truth candidates and target-side Playwright/Kontur POM evidence such as `ControlFactory.Create`, `GetByTestId`, and `Locator("[data-tid...]")`.

```bash
dotnet tool run selenium-pw-migrator -- --mode index-pom --input ./OldTests --out pom-index
```

`helper-inventory`

Inspects helper/POM methods before you map or suppress helper wrappers.

```bash
dotnet tool run selenium-pw-migrator -- --mode helper-inventory --input ./OldTests --out helper-inventory
```

`selector evidence`

Explains Selenium selector → config mapping → generated Playwright locator provenance.

```bash
dotnet tool run selenium-pw-migrator -- selector evidence --input migration/run-001 --config ./adapter-config.json --out selector-evidence
```

`propose`

Creates mapping proposals from migration artifacts without changing config.

```bash
dotnet tool run selenium-pw-migrator -- --mode propose --input migration/generated --config ./adapter-config.json --out proposals
```

### Reports, runtime triage, and sharing

`explain-todo`

Explains remaining TODOs and likely root causes.

```bash
dotnet tool run selenium-pw-migrator -- --mode explain-todo --input migration/verify-project --out todo-explanation
```

`smoke-plan`

Ranks generated tests by runtime readiness.

```bash
dotnet tool run selenium-pw-migrator -- --mode smoke-plan --input migration/verify-project --out smoke-plan
```

`runtime-classify`

Classifies Playwright runtime failures from logs, traces, screenshots, and videos.

```bash
dotnet tool run selenium-pw-migrator -- --mode runtime-classify --input migration/runtime-logs --out runtime-classify
```

`learn pack`

Extracts reusable migration knowledge from a completed run into a reviewable profile layer and learning changelog.

```bash
dotnet tool run selenium-pw-migrator -- learn pack --input migration/run-001 --config ./adapter-config.json --out learn-pack --format both
```

`migration-board`

Builds an HTML dashboard from migration artifacts.

```bash
dotnet tool run selenium-pw-migrator -- --mode migration-board --input migration/run-001 --out board --format both
```

`report serve`

Builds and optionally serves a local dashboard.

```bash
dotnet tool run selenium-pw-migrator -- report serve --input migration/runs/latest --port 5077 --out migration/dashboard/latest
```

Use static-only mode for CI artifacts:

```bash
dotnet tool run selenium-pw-migrator -- report serve --input migration/runs/latest --static-only --out migration/dashboard/latest
```

`evidence pack`

Creates a redacted zip for PRs, issues, or external review.

```bash
dotnet tool run selenium-pw-migrator -- evidence pack --input migration/run-001 --out evidence/run-001.zip
```

Only use `--include-source` after explicit review:

```bash
dotnet tool run selenium-pw-migrator -- evidence pack --input migration/run-001 --out evidence/run-001.zip --include-source
```

`pr pack`

Creates a PR/review bundle: summary, generated files list, before/after metrics, risk summary, reviewer checklist, and suggested PR description.

```bash
dotnet tool run selenium-pw-migrator -- pr pack --input migration/run-001 --out pr-pack --format both
```

`agent contract`

Generates ticket-specific agent instructions with allowed paths, stop policy, exact commands, report template, and coordinator/migrator/verifier prompts.

```bash
dotnet tool run selenium-pw-migrator -- agent contract --input migration/current-ticket.md --out agent-contract --format both
```

## 8. Common Workflows

### I have only Selenium tests and no Playwright project

1. Run `start --input ./OldTests --agent manual --workspace migration`.
2. Run `pilot --input ./OldTests --max-tests 10 --out migration/pilot`.
3. Use `init --wizard` only if you specifically need the legacy `scaffold/` generator.
4. Fill in auth, routes, base URL, and target namespace.
5. Run `index-pom`, `helper-inventory`, and `selector evidence`.
6. Run `migrate` against the pilot `selected-input/` first.
7. Run `verify-project`.

### I already have a Playwright .NET project

1. Run `discover-target`.
2. Copy useful target facts into the adapter config.
3. Run `doctor`.
4. Run `orchestrate`.
5. Use `explain-todo` and `guard` between iterations.

### I want the fastest useful pilot

1. Run `playground` once to understand the flow.
2. Run `start --input ./OldTests --agent manual --workspace migration`.
3. Run `pilot --input ./OldTests --max-tests 10 --out migration/pilot`.
4. Run the generated commands from `migration/pilot/next-commands.md`.
5. Open the dashboard with `report serve` after a run exists.
6. Run `explain-todo` and review `suggested-config-patch.md/json`.
7. Fix the top repeated unsupported category, not random one-off TODOs.

### I want to use agents safely

Give the agent:

- The source test path.
- The allowed output workspace.
- The current config/profile.
- The current migration artifacts.
- A rule: do not edit source tests and do not invent selectors.

Use `start`, `pilot`, and then one of the bootstrap commands:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json --opencode-install auto
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json
```

Use `docs/guarded-opencode-desktop-runbook.ru.md` plus the installed `migration/AGENT_CONTRACT.md` for guarded OpenCode Desktop runs.

## 9. How To Get Better Results

Small inputs beat huge first runs. Start with a slice that contains common patterns.

Profiles beat manual generated-code edits. If the same TODO appears many times, fix the mapping or recognizer once.

Evidence beats guesses. Run `index-pom`, `helper-inventory`, `selector evidence`, and `discover-target` before broad config changes.

Project-aware verification beats syntax checks. Use `verify-project` when targeting Playwright .NET.

Reports are part of the product. Keep `orchestration-report`, `explain-todo`, `guard`, and `evidence-pack` outputs with the migration ticket.

## 10. Troubleshooting

`Input not found`

Check whether `--input` points to the source tests for source-processing modes, or to artifact folders for report modes.

`Config not found`

Use an absolute path or verify that the config path is relative to your current terminal directory.

`Generated code compiles but has many TODOs`

That is a checkpoint, not the end. Run `explain-todo`, then fix the highest-frequency source-truth gaps.

`verify-project fails because packages or references are missing`

Run `doctor`. Then add real project references or package references to the config `Verification` section.

`The output uses NUnit but I need xUnit`

Pass `--target-test-framework xunit` to `init`, `scaffold`, `migrate`, and `verify-project`, or set `TestHost.TargetTestFramework` in adapter config.

`report serve should not start a server in CI`

Use `--static-only` or `--port 0`.

## 11. Output Locations

Relative `--out` paths are usually written under `migration/`.

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze --out analysis
```

Usually writes:

```text
migration/analysis/
```

Absolute paths are respected:

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze --out C:/temp/migrator-analysis
```

For shareable output, prefer `evidence pack`.

## 12. What Migrator Should Not Do

Migrator should not silently invent selectors.

Migrator should not hide uncertain behavior in generated code.

Migrator should not require you to manually patch every generated file.

Migrator should not modify your source Selenium tests.

Migrator should not claim runtime success before the target environment, auth, data, and routes are configured.

The best migration is not the one with the fewest TODOs. It is the one where every generated line is either correct, verified, or clearly marked for review.


## Developer bootstrap smoke

To verify that `kit bootstrap-opencode` does not accidentally use `templates/migration-kit` from a product repository, run:

```powershell
pwsh .\scripts\run-kitroot-shadow-smoke.ps1 -Clean
```

The smoke creates a fake product repo with a shadow `templates/migration-kit` directory and fails if that directory is used as the kit root.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.


### Wavefront / memory / config-merge snapshot

When you are using project-scoped memory and divide-and-conquer waves, still start review from the dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

The report includes a **Wavefront / memory / config-merge snapshot**. It summarizes project-scoped memory, wavefront progress, next wave candidates, candidate config status, and open `conflicts.jsonl` items. This is read-only: it does not promote memory, merge config into the active adapter config, or mark a wave complete.
