# Migration Workflow

A complete guide for migrating Selenium C# tests to Playwright .NET using the Migrator.

## Overview

Migration is an iterative process:

```
analyze → configure profile → migrate → verify → propose → iterate
```

Each iteration improves the quality of generated code. The goal is not one perfect pass, but a controlled loop that converges on working Playwright tests.

## Choosing your path

Before starting, determine which path applies to your team:

| Path | Situation | Starting mode |
|---|---|---|
| **Path A: Existing Playwright infra** | You have a Playwright .NET project with tests, base classes, and auth flow | `discover-target` → review draft config → `orchestrate` |
| **Path B: No Playwright infra** | You have Selenium tests but no Playwright .NET project at all | `scaffold` → implement auth/routes → review draft config → `migrate`/`verify` |

See [No-Infra Scaffold](no-infra-scaffold.md) for details on Path B.

## Step 1. Start with a representative pilot

Do not start with your largest or most complex test suite. Let the CLI choose a bounded representative slice first:

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent manual --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

The pilot command writes:

- `migration/pilot/pilot-selection.md/json` — selected files and reasons.
- `migration/pilot/selected-tests.txt` — the exact source files.
- `migration/pilot/selected-input/` — copied bounded input.
- `migration/pilot/next-commands.md` — analyze/migrate commands for `selected-input`.

The generated next commands must not run on the full suite. Use the pilot `selected-input/` until the profile and target-project checks are stable.

**Selection strategy:**

- simple smoke tests;
- PageObject-heavy files;
- table/filter patterns;
- assertions and waits;
- custom helpers;
- XPath selectors;
- data-driven tests;
- base fixtures.

**Iteration strategy:**

1. Prove the selected pilot.
2. Fix repeated root causes with profile mappings.
3. Expand to 20-50 tests.
4. Tackle complex patterns last.

## Step 2. Run analyze

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode analyze --input "./SeleniumTests" --out "./analysis" --format both
```

Review:
- `analysis/unmapped-targets.json` — which elements need profile mappings
- `analysis/unsupported-actions.json` — which actions need manual migration or method mappings
- `analysis/report.txt` — overall coverage
- `analysis/migration-quality-dashboard.md` — root causes, guardrails, and next safe quality tickets

Identify the most frequently occurring unmapped targets and the first P0/P1 item in `migration-quality-tickets.md`. These give the highest return on config or recognizer effort.

## Step 3. Add source-truth mappings

Open `adapter-config.json` and add UiTarget mappings for the top unmapped targets.

**Critical: locate selectors only from source truth.**
Source truth means:
- PageObject C# source code (methods like `WithDataTestId`, `WithDataTest`, `WithDataTid`)
- Actual HTML attributes (if you can verify them)
- Target project's existing Playwright tests (`target-inventory.json` from `discover-target` mode)

**Do not:**
- Invent selectors
- Guess attribute values
- Use values from discovery draft without verification

Example:

```json
{
  "SourceExpression": "page.SearchButton",
  "TargetExpression": "t_search",
  "TargetKind": "TestId"
}
```

## Step 4. Generate code

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode migrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./generated" --format both
```

Review `generated/report.json` for:
- `GeneratedFiles` — number of files generated
- `Mapped` / `Unmapped` — how many targets were resolved
- `Unsupported` — actions that need manual attention

## Step 5. Verify generated output

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode verify --input "./generated" --config "./adapter-config.json" --out "./verify" --format both
```

Review `verify/verify-report.json`:
- `summary.status` — `passed` or `failed`
- `summary.syntaxErrors` — C# syntax errors
- `summary.todoComments` — TODO comments in generated code
- `summary.placeholderLeftovers` — unresolved `{placeholder}` tokens
- `files[]` — per-file issues

## Step 6. Compile smoke test

Copy generated files into a Playwright .NET test project and run:

```bash
dotnet build
```

Fix any compilation errors. Common issues:
- Missing `using` directives
- Wrong `SetUpStatements` for your test host
- Unresolved method calls that need `MethodMapping`

## Step 7. Runtime proof

Run the generated tests against a real environment:

```bash
dotnet test --filter "FullyQualifiedName~YourTestClass"
```

If tests fail, classify each failure (see [Failure Classification](#failure-classification)).

## Step 8. Use propose to pick next mappings

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode propose --input "./generated" --config "./adapter-config.json" --out "./proposals" --format both
```

Review `proposals/mapping-proposals.md`. Start with the highest-priority proposals:
- `UiTarget` proposals that reduce unmapped count
- `MethodMapping` proposals for frequent helpers
- `ParameterizedMethodMapping` for helpers with varying arguments

Apply one small mapping group at a time, re-run verify, and confirm improvement.

## Step 9. Iterate

Repeat steps 3-8 until:
- All target files generate clean code
- Compile smoke passes
- Runtime tests pass
- Quality gates are satisfied

## Failure classification

When tests fail at runtime, classify each failure into one category:

| Category | Cause | Who fixes |
|---|---|---|
| **Generated code bug** | The Migrator produced incorrect C# | Tool fix or manual edit |
| **Profile issue** | Missing or wrong config mapping | Add/fix adapter-config.json |
| **Wrong locator** | Target expression doesn't match real page | Verify source truth, fix mapping |
| **Helper semantics** | Helper behavior not captured by mapping | Add MethodMapping or ParameterizedMethodMapping |
| **Table/list strategy** | Row access or assertion pattern differs | Configure table/list mappings or manual edit |
| **Test data** | Required data not present in test environment | Data setup (outside Migrator scope) |
| **Environment/backend** | Auth, network, or service issue | Infrastructure (outside Migrator scope) |
| **Manual migration required** | Complex logic that cannot be auto-mapped | Developer writes by hand |

## Quality gates for production

Once the pilot is stable, tighten quality gates in `adapter-config.json`:

```json
{
  "QualityGates": {
    "MaxTodoComments": 0,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0,
    "MaxRawExpressions": 0,
    "FailOnInvalidGeneratedSyntax": true,
    "FailOnPlaceholderLeftovers": true
  }
}
```

See [Reports & Quality Gates](reports-and-quality-gates.md) for details.

## Orchestrate mode shortcut

For iterative development, use orchestrate mode to run the full pipeline:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- run --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
```

Review `orchestration/orchestration-report.md` after each run. Apply one high-priority proposal, then re-run.

## Scaling to larger batches

When the pilot is proven:
1. Select next batch of 20-50 tests
2. Copy the proven adapter-config.json as a starting point
3. Add scope-specific overrides using `Scopes` in the config
4. Run orchestrate on the new batch
5. Address new unmapped targets and unsupported actions
6. Tighten quality gates as needed
