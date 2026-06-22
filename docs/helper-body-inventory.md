# Helper Body Inventory

`helper-inventory` is an agent-safety analysis mode for Selenium helper/POM layers.

It does **not** migrate helper source files and does **not** modify adapter config. It scans helper method bodies and produces reviewable evidence for `MethodSemantics` and mapping decisions.

## Why this exists

A test often calls a project helper such as:

```csharp
page.BillNumber.InputAndAccept("21931973650K1");
page.Loader.ValidateLoading();
```

The important Selenium behavior may live inside the helper body (`SendKeys`, `Keys.Enter`, `WebDriverWait`, `FindElement`, `ExpectedConditions`, etc.). If the migration engine only sees the top-level test call, an agent may incorrectly suppress a required action or leave a downstream assertion active after losing a side effect.

`helper-inventory` gives the next agent source-truth evidence before it edits config.

## Command

```powershell
selenium-pw-migrator --mode helper-inventory \
  --input "C:\path\to\selenium_tests" \
  --out "migration/helper-inventory" \
  --format both
```

Cross-platform:

```bash
selenium-pw-migrator --mode helper-inventory \
  --input ./selenium_tests \
  --out migration/helper-inventory \
  --format both
```

## Outputs

- `helper-inventory.md` — human-readable grouped report.
- `helper-inventory.json` — machine-readable evidence.
- `method-semantics.candidates.json` — review-only config draft.
- `agent-helper-semantics-task.md` — bounded task prompt for Codex/OpenCode.

## Semantics

The inventory infers one of these review labels:

- `RequiredSideEffect` — input/click/select/navigation/save/delete-like helper. Do not suppress.
- `ProjectWaitHelper` — project-specific loader/ajax/table wait. Prefer explicit target helper mapping.
- `SafeWaitElide` — Selenium wait ceremony that Playwright likely covers with auto-wait/assertion retry.
- `ReadOnlyProbe` — visibility/existence/count/text probe.
- `AssertionHelper` — custom assertion helper requiring assertion mapping/manual review.
- `UnknownUnsafe` — not enough evidence; treat as unsafe until classified.

## Important safety rule

`method-semantics.candidates.json` is a draft. Do not auto-merge it. A human or source-aware agent must review the top families, especially `RequiredSideEffect` and `UnknownUnsafe`.

## Receiverless helper calls

The C# parser preserves receiverless project helper calls such as:

```csharp
CreateDopCalc(lightbox);
CreateSubordination(lightbox);
```

as structured `MethodInvocationAction` items when semantic resolution is unavailable. This is intentional: these calls often live in test base classes or helper layers that are not referenced by the lightweight parser compilation. Keeping them structured lets `Methods`, `ParameterizedMethods`, and helper-inventory evidence classify them later instead of collapsing them into generic unsupported/raw statements.

If such a helper has no mapping, generated output uses `MIGRATOR:HELPER_METHOD_REQUIRES_MAPPING` and tells the next agent to run `--mode helper-inventory` or inspect the helper body before suppressing it.
