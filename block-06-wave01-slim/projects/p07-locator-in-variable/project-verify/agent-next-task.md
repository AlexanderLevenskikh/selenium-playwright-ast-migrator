# Agent Next Task

Ты продолжаешь миграцию Selenium C# → Playwright .NET через AST Migrator.
Работай как bounded batch: сначала проверь контекст и gates, затем сделай один измеримый шаг, обнови артефакты и handoff.

## Run context

- Artifact root: `pipeline`
- Artifact lookup: `direct-only`
- Project verify: `passed`
- Files/tests/actions: `1` / `1` / `3`
- TODO/unmapped/unsupported: `3` / `1` / `0`
- Syntax/compile diagnostics: `0`

## Quality gates / safety signals

- EMPTY_TEST_AFTER_SUPPRESSION: `0`
- DEPENDS_ON_SUPPRESSED_SIDE_EFFECT: `0`
- Helper/POM semantics signals: `1`
- Gate: helper/POM wrappers are involved — run or inspect `--mode helper-inventory` before adding suppressions or MethodSemantics guesses.

## Exact next task

Priority: `P2_ROOT_CAUSE`
Category: `MISSING_MAPPING`

Task: **Add mapping for WebDriver.FindElement(target)**

Why: Source expression was not mapped to a Playwright locator/action.

Action: Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.

Representative example: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:12`

Evidence:
- `Suggested target draft: TODO_findElement(target)`

## Top root-cause candidates

| # | Category | Impact | Example | Suggested action |
|---|---|---:|---|---|
| 1 | `MISSING_MAPPING` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:12` | Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json. |
| 2 | `RAW_STATEMENT` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:19` | If repeated, add Method/ParameterizedMethod mapping; otherwise keep manual TODO. |
| 3 | `UNAVAILABLE_SYMBOLS` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:22` | Add TargetKnownTypes/TargetKnownIdentifiers only for real target symbols; otherwise map/comment the expression. |
| 4 | `UNRESOLVED_SYMBOL` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:25` | Find the first TODO that blocked the symbol; fix that root cause first. |

## Top normalized root causes

| # | Category | Group | Count | Suggested action |
|---|---|---|---:|---|
| 1 | `MISSING_MAPPING` | Add mapping for WebDriver.FindElement(target) | 1 | Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json. |
| 2 | `RAW_STATEMENT` | RAW_STATEMENT: method family `Id` | 1 | Group all occurrences of this helper/method family; inspect source/helper body or run --mode helper-inventory before adding MethodSemantics/ParameterizedMethods. |
| 3 | `UNAVAILABLE_SYMBOLS` | UNAVAILABLE_SYMBOLS: source-only root `unknown-root` | 1 | Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code. |
| 4 | `UNRESOLVED_SYMBOL` | UNRESOLVED_SYMBOL: source-only root `unknown-root` | 1 | Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code. |

## Commands to run / update

Use concrete project paths from the current migration workspace; do not point report commands at a parent folder containing multiple runs.

```powershell
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode explain-todo --input "pipeline" --out "<next-explain-out>" --format both
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode migration-board --input "pipeline" --out "<next-board-out>" --format both
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode helper-inventory --input "<selenium-tests-or-helper-root>" --out "<helper-inventory-out>" --format both
dotnet test Migrator.Tests/Migrator.Tests.csproj
```

## Helper inventory rule

Run/request `--mode helper-inventory` before changing suppressions or MethodSemantics for project/POM wrappers such as `InputAndAccept`, `ValidateLoading`, `ClickAndOpen`, `ManualInputValue`, unqualified helper calls, or unknown business helpers. Do not infer helper semantics by name alone.

## Acceptance criteria

- If helper/POM wrappers are touched, helper-inventory evidence is generated or explicitly cited.
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
