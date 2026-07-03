# Migrator User Guide

Migrator is a command-line toolkit for moving Selenium end-to-end tests to Playwright in a controlled, reviewable way.

It does not try to pretend that a whole test suite can be converted perfectly in one click. Instead, it gives you a repeatable loop:

```text
inspect the old tests -> collect source truth -> generate Playwright code -> verify -> improve the profile -> repeat
```

The main production path is Selenium C# to Playwright .NET. NUnit is the default target test framework, and xUnit is also supported. Playwright TypeScript, Java Selenium input, and Python Selenium input exist as preview paths.

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

### Fast start from NuGet

When the package is published, the best team-friendly setup is a project-local dotnet tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator --version 0.0.0
dotnet tool run selenium-pw-migrator -- --help
```

Then create the disposable demo playground:

```bash
dotnet tool run selenium-pw-migrator -- playground \
  --out playground \
  --target-test-framework xunit \
  --generation-policy conservative
```

Open `migration/playground/try-this-first.md` and run the generated command chain before touching a real project.

Relative `--out playground` is written under the default `migration/` workspace. Use an absolute `--out` path if you want the playground somewhere else.

If you want to understand the AST-based migration model before using the tool on a real suite, open the teaching demo and article:

- `examples/teaching-demo/README.md`
- `docs/articles/ast-migration-explained.md`
- `docs/articles/ast-migration-explained.ru.md`

### Install from a locally packed package

If you are validating a release candidate before publishing it:

```bash
./scripts/pack-tool.sh 0.0.0
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator \
  --version 0.0.0 \
  --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

On Windows, use `./scripts/pack-tool.ps1 -Version 0.0.0`.

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

Start with the playground or a small folder of representative tests. Do not begin with the entire suite.

```bash
dotnet tool run selenium-pw-migrator -- playground \
  --out playground \
  --target-test-framework xunit \
  --generation-policy conservative
```

For a real project, generate a runbook before the first migration run:

```bash
dotnet tool run selenium-pw-migrator -- runbook \
  --input ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --generation-policy conservative \
  --out runbook \
  --format both
```

Then create the migration workspace. For an agent-assisted guarded run, use the kit command instead of manually creating folders:

```bash
dotnet tool run selenium-pw-migrator -- kit update \
  --workspace migration \
  --source ./OldTests \
  --config migration/profiles/adapter-config.json \
  --backup \
  --with-team

dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

This creates a safe migration workspace with:

- `profiles/adapter-config.json` - starter profile.
- `current-ticket.md` - the current migration scope.
- `state/run-ledger.md` - a place to record runs.
- `state/harness-policy.json` - the autopilot allow/ask/deny policy.
- `scripts/new-harness-run.ps1` - active run bootstrapper.
- `scripts/check-harness-policy.ps1` - harness policy gate.
- `scripts/build-harness-dashboard.ps1` - static harness dashboard generator.
- `opencode-team/` - optional OpenCode agents and commands when `--with-team` is used.

For agent-assisted runs, the preferred portable bootstrap from the product repo root is:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./OldTests --config migration/profiles/adapter-config.json --opencode-install auto
```

Install modes:

```text
--opencode-install auto             Windows => project-desktop; macOS/Linux/WSL => project-local
--project-desktop                   shortcut for Windows OpenCode Desktop
--opencode-install project-local    portable OpenCode CLI config in .opencode-migrator
--opencode-install ci               Codex/CI/manual agents; no OpenCode config install
```

After bootstrap, start OpenCode with `/supervised-task` when using OpenCode, or give the kickoff prompt to another agent. The supervised agent should create or resume `migration/runs/<run-id>/` with `new-harness-run.ps1`; the user should not need to create run folders manually. For non-OpenCode agents, give the agent `migration/AGENT_CONTRACT.md`, `migration/prompts/kickoff-prompt.txt`, and `migration/harness/README.md`. See `docs/agent-environments.md`.

If you are working without an agent and only want a starter config/scaffold, `init --wizard` is still available:

```bash
dotnet tool run selenium-pw-migrator -- init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --workspace migration
```

If you prefer xUnit output:

```bash
dotnet tool run selenium-pw-migrator -- init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework xunit \
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
dotnet tool run selenium-pw-migrator -- playground verify --input migration/playground --out playground-verify
```

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

`init`

Creates a migration workspace and starter config.

```bash
dotnet tool run selenium-pw-migrator -- init --wizard --source-path ./OldTests --target dotnet --target-test-framework xunit
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
dotnet tool run selenium-pw-migrator -- report serve --input migration/run-001 --port 5077 --out report-dashboard
```

Use static-only mode for CI artifacts:

```bash
dotnet tool run selenium-pw-migrator -- report serve --input migration/run-001 --static-only --out report-dashboard
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

1. Run `init --wizard`.
2. Use generated `scaffold/`.
3. Fill in auth, routes, base URL, and target namespace.
4. Run `index-pom`, `helper-inventory`, and `selector evidence`.
5. Run `migrate`.
6. Run `verify-project`.

### I already have a Playwright .NET project

1. Run `discover-target`.
2. Copy useful target facts into the adapter config.
3. Run `doctor`.
4. Run `orchestrate`.
5. Use `explain-todo` and `guard` between iterations.

### I want the fastest useful pilot

1. Run `playground` once to understand the flow.
2. Pick 10 to 30 representative tests.
3. Run `runbook`.
4. Run `init --wizard`.
5. Run `orchestrate`.
6. Open the report dashboard with `report serve`.
7. Fix the top repeated unsupported category, not random one-off TODOs.

### I want to use agents safely

Give the agent:

- The source test path.
- The allowed output workspace.
- The current config/profile.
- The current migration artifacts.
- A rule: do not edit source tests and do not invent selectors.

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
