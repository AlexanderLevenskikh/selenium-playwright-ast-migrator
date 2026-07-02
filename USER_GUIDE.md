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
dotnet tool install SeleniumPlaywrightMigrator --version 0.6.0-preview.1
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

If you want to understand the AST-based migration model before using the tool on a real suite, open the teaching demo and article:

- `examples/teaching-demo/README.md`
- `docs/articles/ast-migration-explained.md`
- `docs/articles/ast-migration-explained.ru.md`

### Install from a locally packed package

If you are validating a release candidate before publishing it:

```bash
./scripts/pack-tool.sh 0.6.0-preview.1
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator \
  --version 0.6.0-preview.1 \
  --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

On Windows, use `./scripts/pack-tool.ps1 -Version 0.6.0-preview.1`.

### Run from source

From the repository:

```bash
dotnet restore
dotnet run --project Migrator.Cli -- --help
```

Most examples below use `selenium-pw-migrator`. When running from source, replace it with:

```bash
dotnet run --project Migrator.Cli -- 
```

For example:

```bash
dotnet run --project Migrator.Cli -- --mode analyze --input ./OldTests --out analysis
```

## 3. Recommended First Run

Start with the playground or a small folder of representative tests. Do not begin with the entire suite.

```bash
selenium-pw-migrator playground \
  --out playground \
  --target-test-framework xunit \
  --generation-policy conservative
```

For a real project, generate a runbook before the first migration run:

```bash
selenium-pw-migrator runbook \
  --input ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --generation-policy conservative \
  --out runbook \
  --format both
```

Then create the migration workspace:

```bash
selenium-pw-migrator init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --workspace migration
```

This creates a safe migration workspace with:

- `profiles/adapter-config.json` - starter profile.
- `current-ticket.md` - the current migration scope.
- `state/run-ledger.md` - a place to record runs.
- `next-commands.md` - exact next commands.
- `scaffold/` - optional Playwright .NET scaffold when no target project exists.

If you prefer xUnit output:

```bash
selenium-pw-migrator init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration
```

## 4. The Normal Migration Loop

For most projects, use this sequence.

### Step 1. Check the input and config

```bash
selenium-pw-migrator --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out doctor \
  --format both
```

Use `doctor --fix` to create safe repair plans and candidate files:

```bash
selenium-pw-migrator --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --dry-run \
  --out doctor-fix
```

Use `--apply` only when you are comfortable with the proposed safe workspace/config candidate writes:

```bash
selenium-pw-migrator --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --apply \
  --out doctor-fix
```

### Step 2. Collect source truth

Run this before large PageObject or helper mapping work:

```bash
selenium-pw-migrator --mode index-pom \
  --input ./OldTests \
  --out pom-index \
  --format both
```

Then inspect helper wrappers:

```bash
selenium-pw-migrator --mode helper-inventory \
  --input ./OldTests \
  --out helper-inventory \
  --format both
```

If you already have a Playwright .NET project, inspect it too:

```bash
selenium-pw-migrator --mode discover-target \
  --input ./PlaywrightTests \
  --out target-discovery \
  --format both
```

### Step 3. Analyze without generating code

```bash
selenium-pw-migrator --mode analyze \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out run-001-analysis \
  --format both
```

Read the report. It tells you what Migrator understood, what it could not map, and which unsupported patterns appear often.

### Step 4. Generate Playwright tests

For NUnit:

```bash
selenium-pw-migrator --mode migrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out run-001-generated \
  --format both
```

For xUnit:

```bash
selenium-pw-migrator --mode migrate \
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
selenium-pw-migrator --mode verify \
  --input migration/run-001-generated \
  --config migration/profiles/adapter-config.json \
  --out run-001-verify \
  --format both
```

For Playwright .NET, run project-aware verification:

```bash
selenium-pw-migrator --mode verify-project \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target-test-framework nunit \
  --out run-001-verify-project \
  --format both
```

For Playwright TypeScript:

```bash
selenium-pw-migrator --mode verify-ts-project \
  --input migration/run-001-generated \
  --ts-project ./PlaywrightTsProject \
  --out run-001-verify-ts \
  --format both
```

### Step 6. Explain what remains

```bash
selenium-pw-migrator --mode explain-todo \
  --input migration/run-001-verify-project \
  --out run-001-explain \
  --format both
```

Then use the highest-impact categories to improve the profile or Migrator behavior.

### Step 7. Compare before and after

```bash
selenium-pw-migrator --mode guard \
  --before migration/baseline \
  --after migration/run-001-verify-project \
  --out run-001-guard \
  --format both
```

Guard is useful in CI and agent loops because it catches regressions in TODOs, unsupported actions, syntax errors, and other migration metrics.

## 5. One-Command Orchestration

When the basic setup is ready, `orchestrate` runs the common dry-run workflow:

```bash
selenium-pw-migrator --mode orchestrate \
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
selenium-pw-migrator --mode scaffold \
  --target-test-framework xunit \
  --out generated-scaffold
```

```bash
selenium-pw-migrator --mode migrate \
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
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

`playground-verify`

Checks that a generated playground still contains the public demo contract: manifest, ready command chain, sample Selenium input, adapter config, expected Playwright output, dashboard sample, PR pack sample, and selector-safety wording.

```bash
selenium-pw-migrator playground verify --input playground --out playground-verify
```

`release-doctor`

Checks NuGet preview readiness from the repository root: package metadata, version/changelog consistency, release scripts, README_TOOL packaging docs, publish workflow dry-run support, NuGet secret references, and repository hygiene.

```bash
selenium-pw-migrator doctor release --out release-doctor
```

`runbook`

Generates a practical migration plan: pilot scope, command chain, risk map, artifacts to collect, and acceptance checklist.

```bash
selenium-pw-migrator runbook --input ./OldTests --target dotnet --target-test-framework xunit --generation-policy conservative --out runbook
```

`init`

Creates a migration workspace and starter config.

```bash
selenium-pw-migrator init --wizard --source-path ./OldTests --target dotnet --target-test-framework xunit
```

`doctor`

Checks whether the input, config, environment, and workspace are ready. With `--fix`, writes safe repair plans or candidate files.

```bash
selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --fix --dry-run --out doctor
```

`capabilities`

Lists available source frontends and target backends.

```bash
selenium-pw-migrator --mode capabilities --out capabilities --format both
```

`framework matrix`

Writes source framework detection and target framework readiness reports.

```bash
selenium-pw-migrator framework matrix --input ./OldTests --target dotnet --target-test-framework xunit --out framework-matrix --format both
```

`discover-target`

Scans an existing Playwright .NET project and describes its namespaces, base classes, packages, and reusable infrastructure.

```bash
selenium-pw-migrator --mode discover-target --input ./PlaywrightTests --out target-discovery
```

`scaffold`

Creates a minimal Playwright .NET test project skeleton.

```bash
selenium-pw-migrator --mode scaffold --target-test-framework nunit --out scaffold
```

`bootstrap-project`

Creates reusable migration profile skeletons for a new project.

```bash
selenium-pw-migrator --mode bootstrap-project --input ./OldTests --out bootstrap-oldtests
```

### Analysis and generation

`analyze`

Parses Selenium tests and reports what can be migrated.

```bash
selenium-pw-migrator --mode analyze --input ./OldTests --config ./adapter-config.json --out analysis
```

`migrate`

Generates Playwright output.

```bash
selenium-pw-migrator --mode migrate --input ./OldTests --config ./adapter-config.json --target dotnet --generation-policy balanced --out generated
```

Use `--generation-policy conservative|balanced|aggressive` to control mapped-helper risk. Conservative emits more review/TODO output, balanced is the normal default, and aggressive emits more active helper code with risk annotations.

`dump-ir`

Maintainer/debug command that dumps internal representation.

```bash
selenium-pw-migrator --mode dump-ir --input ./OldTests --config ./adapter-config.json --out ir --ir-version both
```

`orchestrate`

Runs analyze, migrate, verify, and proposal generation as one dry-run flow.

```bash
selenium-pw-migrator --mode orchestrate --input ./OldTests --config ./adapter-config.json --out run-001
```

### Verification and quality gates

`verify`

Checks generated code and reports syntax/TODO/config issues.

```bash
selenium-pw-migrator --mode verify --input migration/generated --config ./adapter-config.json --out verify
```

`verify-project`

Builds generated Playwright .NET output in a temporary project-aware harness.

```bash
selenium-pw-migrator --mode verify-project --input ./OldTests --config ./adapter-config.json --out verify-project
```

`verify-ts-project`

Type-checks generated Playwright TypeScript specs inside an existing TS project.

```bash
selenium-pw-migrator --mode verify-ts-project --input migration/generated-ts --ts-project ./PlaywrightTs --out verify-ts
```

`guard`

Compares two runs and fails on regressions.

```bash
selenium-pw-migrator --mode guard --before migration/baseline --after migration/current --out guard
```

### Config and profile work

`config-schema`

Writes the JSON Schema for adapter config.

```bash
selenium-pw-migrator --mode config-schema --out schema
```

`config-validate`

Validates adapter config structure and safety rules.

```bash
selenium-pw-migrator --mode config-validate --config ./adapter-config.json --validation-mode strict --out config-check
```

`config-normalize`

Maintainer command that converts older config shape into the newer profile shape.

```bash
selenium-pw-migrator --mode config-normalize --config ./adapter-config.json --out normalized
```

`config-diff`

Compares two configs and highlights risky changes.

```bash
selenium-pw-migrator --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff
```

`profile list`

Lists built-in offline profiles.

```bash
selenium-pw-migrator profile list
```

`profile search`

Finds profiles by framework, backend, or capability.

```bash
selenium-pw-migrator profile search xunit
```

`profile recommend`

Scores built-in profiles against a source project and recommends install order.

```bash
selenium-pw-migrator profile recommend --input ./OldTests --target-test-framework xunit --out profile-recommendations
```

`profile inspect`

Explains a built-in profile before installation.

```bash
selenium-pw-migrator profile inspect basic-csharp-xunit
```

`profile install`

Installs a built-in profile as a config layer.

```bash
selenium-pw-migrator profile install basic-csharp-nunit --out profiles
```

`profile diff`

Compares a config to another config or built-in profile.

```bash
selenium-pw-migrator profile diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff
```

`profile-match`

Estimates whether existing config/profile layers fit a source project.

```bash
selenium-pw-migrator --mode profile-match --input ./OldTests --config ./profiles/base.adapter.json --out profile-match
```

`config author`

Writes evidence-driven config proposals and a reviewable patch from selector evidence, POM index, helper inventory, target discovery, and TODO reports. It does not apply the patch.

```bash
selenium-pw-migrator config author --input migration/run-001 --config ./adapter-config.json --out config-proposals --format both
```

### Source truth helpers

`index-pom`

Finds Selenium PageObject selectors/source-truth candidates and target-side Playwright/Kontur POM evidence such as `ControlFactory.Create`, `GetByTestId`, and `Locator("[data-tid...]")`.

```bash
selenium-pw-migrator --mode index-pom --input ./OldTests --out pom-index
```

`helper-inventory`

Inspects helper/POM methods before you map or suppress helper wrappers.

```bash
selenium-pw-migrator --mode helper-inventory --input ./OldTests --out helper-inventory
```

`selector evidence`

Explains Selenium selector → config mapping → generated Playwright locator provenance.

```bash
selenium-pw-migrator selector evidence --input migration/run-001 --config ./adapter-config.json --out selector-evidence
```

`propose`

Creates mapping proposals from migration artifacts without changing config.

```bash
selenium-pw-migrator --mode propose --input migration/generated --config ./adapter-config.json --out proposals
```

### Reports, runtime triage, and sharing

`explain-todo`

Explains remaining TODOs and likely root causes.

```bash
selenium-pw-migrator --mode explain-todo --input migration/verify-project --out todo-explanation
```

`smoke-plan`

Ranks generated tests by runtime readiness.

```bash
selenium-pw-migrator --mode smoke-plan --input migration/verify-project --out smoke-plan
```

`runtime-classify`

Classifies Playwright runtime failures from logs, traces, screenshots, and videos.

```bash
selenium-pw-migrator --mode runtime-classify --input migration/runtime-logs --out runtime-classify
```

`learn pack`

Extracts reusable migration knowledge from a completed run into a reviewable profile layer and learning changelog.

```bash
selenium-pw-migrator learn pack --input migration/run-001 --config ./adapter-config.json --out learn-pack --format both
```

`migration-board`

Builds an HTML dashboard from migration artifacts.

```bash
selenium-pw-migrator --mode migration-board --input migration/run-001 --out board --format both
```

`report serve`

Builds and optionally serves a local dashboard.

```bash
selenium-pw-migrator report serve --input migration/run-001 --port 5077 --out report-dashboard
```

Use static-only mode for CI artifacts:

```bash
selenium-pw-migrator report serve --input migration/run-001 --static-only --out report-dashboard
```

`evidence pack`

Creates a redacted zip for PRs, issues, or external review.

```bash
selenium-pw-migrator evidence pack --input migration/run-001 --out evidence/run-001.zip
```

Only use `--include-source` after explicit review:

```bash
selenium-pw-migrator evidence pack --input migration/run-001 --out evidence/run-001.zip --include-source
```

`pr pack`

Creates a PR/review bundle: summary, generated files list, before/after metrics, risk summary, reviewer checklist, and suggested PR description.

```bash
selenium-pw-migrator pr pack --input migration/run-001 --out pr-pack --format both
```

`agent contract`

Generates ticket-specific agent instructions with allowed paths, stop policy, exact commands, report template, and coordinator/migrator/verifier prompts.

```bash
selenium-pw-migrator agent contract --input migration/current-ticket.md --out agent-contract --format both
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
selenium-pw-migrator --mode analyze --out analysis
```

Usually writes:

```text
migration/analysis/
```

Absolute paths are respected:

```bash
selenium-pw-migrator --mode analyze --out C:/temp/migrator-analysis
```

For shareable output, prefer `evidence pack`.

## 12. What Migrator Should Not Do

Migrator should not silently invent selectors.

Migrator should not hide uncertain behavior in generated code.

Migrator should not require you to manually patch every generated file.

Migrator should not modify your source Selenium tests.

Migrator should not claim runtime success before the target environment, auth, data, and routes are configured.

The best migration is not the one with the fewest TODOs. It is the one where every generated line is either correct, verified, or clearly marked for review.
