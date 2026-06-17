# POM indexing and inferred POM candidates

POMs are source truth for Selenium → Playwright migration. Tests usually say **what** is used (`page.SaveButton`), while PageObjects say **how** to find it in UI (`By.CssSelector("[data-tid='SaveButton']")`).

Use `index-pom` before large adapter-config work:

```powershell
dotnet run --project .\Migrator.Cli -- --mode index-pom --input "C:\path\to\SeleniumProject" --out "pom-index" --format both
```

The mode writes:

- `pom-index.generated.json` — source-truth facts found in PageObjects/source files;
- `pom-index.generated.md` — human-readable summary;
- `inferred-pom-candidates.json` — usages like `page.SaveButton` that were seen but not backed by a found POM selector;
- `adapter-config.pom-draft.json` — review-only draft with high-confidence facts and inferred candidates separated.

## Important safety rule

`pom-index.generated.json` and `adapter-config.pom-draft.json` are not a replacement for `adapter-config.json`.

High-confidence facts may be copied into `adapter-config.json` after review. Inferred candidates must not be auto-merged.

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
3. Copy only reviewed/high-confidence UiTarget facts to `adapter-config.json`.
4. For inferred candidates, search source truth. If not found, write an escalation note.
5. Re-run migration and compare TODO/unmapped metrics.

