# Agent Next Task

Ты продолжаешь миграцию Selenium C# → Playwright .NET через AST Migrator.
Работай как bounded batch: сначала проверь контекст и gates, затем сделай один измеримый шаг, обнови артефакты и handoff.

## Run context

- Artifact root: `pipeline`
- Artifact lookup: `direct-only`
- Project verify: `passed`
- Files/tests/actions: `1` / `1` / `8`
- TODO/unmapped/unsupported: `3` / `0` / `0`
- Syntax/compile diagnostics: `0`

## Quality gates / safety signals

- EMPTY_TEST_AFTER_SUPPRESSION: `0`
- DEPENDS_ON_SUPPRESSED_SIDE_EFFECT: `0`
- Helper/POM semantics signals: `0`

## Exact next task

Priority: `P2_ROOT_CAUSE`
Category: `ASSERTION_CONSTRAINT`

Task: **Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion**

Why: The assertion was preserved because no direct Playwright assertion mapping was inferred.

Action: Add reusable assertion mapping if this pattern appears often.

Representative example: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p06-checkbox-radio-selected-b7e62e093a9442489545b0bb8ac3b4c8\.migration-input\Tests\FormStateTests.cs:21`

Evidence:
- `// TODO: convert constraint to Playwright assertion [MIGRATOR:ASSERTION_CONSTRAINT]`
- `// TODO: convert constraint to Playwright assertion [MIGRATOR:ASSERTION_CONSTRAINT]`
- `// TODO: convert constraint to Playwright assertion [MIGRATOR:ASSERTION_CONSTRAINT]`

## Top root-cause candidates

| # | Category | Impact | Example | Suggested action |
|---|---|---:|---|---|
| 1 | `ASSERTION_CONSTRAINT` | 3 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p06-checkbox-radio-selected-b7e62e093a9442489545b0bb8ac3b4c8\.migration-input\Tests\FormStateTests.cs:21` | Add reusable assertion mapping if this pattern appears often. |

## Top normalized root causes

| # | Category | Group | Count | Suggested action |
|---|---|---|---:|---|
| 1 | `ASSERTION_CONSTRAINT` | Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion | 3 | Add reusable assertion mapping if this pattern appears often. |

## Commands to run / update

Use concrete project paths from the current migration workspace; do not point report commands at a parent folder containing multiple runs.

```powershell
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode explain-todo --input "pipeline" --out "<next-explain-out>" --format both
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode migration-board --input "pipeline" --out "<next-board-out>" --format both
dotnet test Migrator.Tests/Migrator.Tests.csproj
```

## Helper inventory rule

Run/request `--mode helper-inventory` before changing suppressions or MethodSemantics for project/POM wrappers such as `InputAndAccept`, `ValidateLoading`, `ClickAndOpen`, `ManualInputValue`, unqualified helper calls, or unknown business helpers. Do not infer helper semantics by name alone.

## Acceptance criteria

- Focused regression tests are added for engine changes; config-only changes include before/after metrics.
- Generated reports are refreshed from a concrete run directory, not a parent artifact folder.
- Metrics before/after are reported: TODO, unmapped, unsupported, empty tests, suppressed side-effect dependencies.

## Do not do

- Do not edit generated `.cs` files manually.
- Do not add broad suppressions just to reduce TODO count.
- Do not add `page`/`pagef` to known identifiers to hide a root cause.
- Do not mark runtime-ready if project verify is missing or failed.
- Do not guess selectors or helper semantics without source/POM/helper evidence.
- If the fix requires engine code, add focused regression tests and keep the patch small.

## Required final response format

### Summary
### Files changed
### Commands run
### Metrics before/after
### Quality gate status
### Remaining risks
### Next exact task
