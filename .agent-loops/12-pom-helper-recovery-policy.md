# POM and helper recovery policy

This policy is mandatory for migration runs that involve PageObjects, helper wrappers, generated POMs, or raw locator fallback.

## Core rule

Missing Playwright POM coverage is **not** automatically `TICKET_NEEDED`.

If the Selenium POM contains real selector evidence, the agent must first recover that source truth and use it in the allowed migration output.

The agent must not conclude that migration is pointless only because the target Playwright project has few existing POM classes.

## Mandatory evidence commands

Before large POM/config work, run or inspect `index-pom` on the Selenium project or POM directory:

```powershell
<MIGRATOR_TOOL_BUNDLE_PATH>\migrator.exe `
  --mode index-pom `
  --input "<SOURCE_SELENIUM_PROJECT_PATH or POM directory>" `
  --out "<MIGRATION_OUTPUT_ROOT>\pom-index" `
  --format both
```

Before mapping, suppressing, or classifying project/POM helper wrappers, run or inspect `helper-inventory`:

```powershell
<MIGRATOR_TOOL_BUNDLE_PATH>\migrator.exe `
  --mode helper-inventory `
  --input "<SOURCE_SELENIUM_PROJECT_PATH or helper/POM directory>" `
  --out "<MIGRATION_OUTPUT_ROOT>\helper-inventory" `
  --format both
```

If the agent is running against an installed tool instead of `migrator.exe`, use the equivalent `selenium-pw-migrator --mode ...` command.

If the task is compiled-tool-only, use the provided compiled tool path. Do not search for migrator source code just to run these modes.

## Selector source truth

Use only proven selectors from allowed inputs.

Allowed selector evidence includes:

- `ByTId("value")`;
- explicit `data-tid`, `data-testid`, `data-test`, or configured test-id attributes;
- Selenium POM factory helpers such as `CreateControlByTid`, `WithTid`, `WithDataTestId`, `WithDataTest`, `By.CssSelector`, `By.XPath`;
- selector constants resolved from allowed source inputs;
- existing target Playwright POM members, only when they really exist in allowed target/example inputs.

A PageObject class name, property name, or method name is **not** selector evidence.

Do not invent selectors. Do not guess missing `data-tid` values. If selector evidence is missing, emit/report TODO instead.

## Locator/POM decision order

For each repeated POM expression or unresolved PageObject target, choose the first safe option:

1. **Use an existing target Playwright POM member** if it exists in allowed inputs and matches the source semantics.
2. **Generate a Playwright POM scaffold/member inside the migration output folder** using Selenium POM selector evidence.
3. **Use a raw Playwright locator in generated output** when a full POM scaffold is not available or not worth creating, but the selector is proven.
4. **Emit TODO / report blocker** when no selector or helper semantics can be proven.

Raw locator fallback is preferable to blocking migration when the selector is proven and POM architecture is missing.

## Generated POM policy

Generated POMs/candidates are allowed only inside migration/output paths, for example:

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\pom-candidates
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\run-001\generated
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\run-001\scaffold
```

Do not modify production target PageObjects unless the current ticket explicitly allows it.

Existing Playwright POM files are style/convention examples, not a required complete target set. Low target POM coverage is a reason to generate scaffold/candidates, not a reason to stop.

Generated POM members must include or preserve evidence in comments/reports:

```csharp
// Source: DiscountSettingsPage.SaveButton
// Evidence: ByTId("SaveButton")
public ILocator SaveButton => Page.GetByTestId("SaveButton");
```

If the configured test-id attribute is not Playwright's default, use the project's configured helper/locator style instead of blindly using `GetByTestId`.

## Helper semantics policy

Do not infer helper semantics from names such as `CreateDopCalc`, `InputAndAccept`, `ValidateLoading`, `ClickAndOpen`, `ManualInputValue`, or similar.

Use `helper-inventory.md/json` or directly inspected helper bodies from allowed inputs.

Then classify the helper as one of:

- required side effect/action;
- assertion helper;
- wait/synchronization helper;
- navigation/auth/setup helper;
- read-only probe;
- unsafe/manual migration;
- suppressible only with explicit safety rationale.

If helper semantics are unclear, keep an explicit TODO and report a bounded next task. Do not suppress by name alone.

## Reporting requirements

Every migration report touching POMs/helpers must include:

- Selenium POM classes inspected;
- existing Playwright POM examples inspected, if any;
- `index-pom` command/path or explicit reason it could not run;
- `helper-inventory` command/path or explicit reason it could not run;
- POM classes/members generated into output;
- raw locators generated from proven selectors;
- unresolved selector TODOs;
- whether the target POM coverage gap was handled by generated scaffolding, raw locators, or TODOs.
