# POM indexing and inferred POM candidates

POMs are source truth for Selenium → Playwright migration. Tests usually say **what** is used (`page.SaveButton`), while PageObjects say **how** to find it in UI (`By.CssSelector("[data-tid='SaveButton']")`). `index-pom` also understands target-side Playwright/Kontur-style POMs so an existing target project can provide selector evidence and naming conventions without being treated as automatic source mappings.

Use `index-pom` before large adapter-config work:

```powershell
dotnet run --project .\Migrator.Cli -- --mode index-pom --input "C:\path\to\SeleniumProject" --out "pom-index" --format both
```

The mode writes:

- `pom-index.generated.json` — source-truth Selenium facts plus target-side Playwright/Kontur POM facts, each with `FactOrigin`;
- `pom-index.generated.md` — human-readable summary;
- `inferred-pom-candidates.json` — usages like `page.SaveButton` that were seen but not backed by a found POM selector;
- `adapter-config.pom-draft.json` — review-only draft with high-confidence Selenium facts, target-side POM evidence, and inferred candidates separated.


## Target-side Playwright/Kontur POM evidence

When `index-pom` scans an existing Playwright/Kontur POM project, it extracts reviewable target-side facts from patterns such as:

```csharp
ControlFactory.Create<Button>(this, "save-row-cost")
ControlFactory.CreateElementsCollection<Row>(this, "row-item")
WrappedItem.GetByTestId("lock-button")
Page.GetByTestId("MenuItem__root")
WrappedItem.Locator("[data-tid^='row-cost-list-row-']")
WrappedItem.Locator("text=Скидки")
```

These facts are emitted with `FactOrigin: "TargetPlaywrightPom"`. They are useful evidence for target conventions and existing POM members, but they do **not** prove that an old Selenium member maps to that target member. A mapping still needs name/usage/config review.

Text locators are emitted as lower-confidence review-required evidence. CSS `Locator("[data-tid...]")` facts are also review-required because they may encode collection/prefix semantics.

## Important safety rule

`pom-index.generated.json` and `adapter-config.pom-draft.json` are not a replacement for `adapter-config.json`.

High-confidence Selenium facts may be copied into `adapter-config.json` after review. Target-side Playwright/Kontur facts are evidence/conventions first; do not auto-merge them as source mappings. Inferred candidates must not be auto-merged.

## When POMs are missing

If a POM selector is missing, the migrator may produce an inferred candidate such as:

```json
{
  "SourceExpression": "page.SaveButton",
  "SuggestedTargetExpression": "SaveButton",
  "SuggestedTargetKind": "TestId",
  "Confidence": "low",
  "RequiresSourceTruth": true
}
```

This means: “the project may follow a naming convention, but the selector was not found.”

Allowed next steps:

1. Find the real POM/base class/helper that defines the selector.
2. Ask the developer/test owner to confirm the selector.
3. Add a reviewed mapping to `adapter-config.json`.
4. Leave TODO if source truth cannot be found.

Not allowed:

- silently convert inferred candidates into real mappings;
- generate selectors just because the member name looks like a `data-tid`;
- add dummy POMs or dummy declarations to generated code;
- modify Selenium source code or production code.

## Agent workflow

1. Run `index-pom` on the broadest relevant Selenium test project, not only on the test files.
2. Review `pom-index.generated.md`.
3. Copy only reviewed/high-confidence Selenium UiTarget facts to `adapter-config.json`; use target-side Playwright/Kontur facts to validate existing target members and conventions.
4. For inferred candidates, search source truth. If not found, write an escalation note.
5. Re-run migration and compare TODO/unmapped metrics.

