# Playbook: Add Parameterized Method Mapping

## Input artifacts

- `unsupported-actions.json` — from analyze or migrate output
- `adapter-config.json` — current configuration
- Source code of the helper method — for understanding argument patterns

## Goal

Map a helper method that is called with varying arguments using a single parameterized mapping.

## Steps

1. **Identify the pattern** — in `unsupported-actions.json`, look for method calls with the same signature but different arguments.
2. **Verify 3+ occurrences** — parameterized mappings are worth it only when the method is called 3+ times with different arguments.
3. **Determine the placeholder name** — use `{value}` for string arguments, `{arg}` for other types.
4. **Read the helper method source** — understand:
   - Which arguments are passed to which Playwright methods
   - Whether the argument appears inside a string literal or as a raw expression
5. **Write the parameterized mapping:**
   ```json
   {
     "ParameterizedMethods": [
       {
         "SourceMethodPattern": "page.Principal.InputAndSelect({value})",
         "TargetStatements": [
           "await Page.GetByText(\"Наименование\").ClickAsync();",
           "var popup = Page.Locator(\"[data-tid='Popup__root']\").Last;",
           "await popup.Locator(\"input\").FillAsync({value});",
           "await popup.GetByText({value}).ClickAsync();"
         ],
         "RequiresReview": true
       }
     ]
   }
   ```
6. **Verify placeholder placement:**
   - `{value}` outside string literal → will be replaced with raw C# expression
   - `{value}` inside string literal → will be replaced with string content (quotes stripped)
7. **Re-run orchestrate:**
   ```bash
   dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
   ```
8. **Check generated code** — verify each call is correctly parameterized.
9. **Report before/after metrics.**

## What NOT to do

- Do NOT use parameterized mappings for methods with only 1-2 occurrences (use exact mapping instead)
- Do NOT put multiple placeholders in one argument position
- Do NOT nest expressions inside placeholders
- Do NOT assume type safety — placeholder substitution is string-based
- Do NOT add multiple parameterized mappings in one pass

## Acceptance criteria

- All occurrences of the method are correctly mapped
- Each generated call has the correct argument substituted
- No unresolved `{placeholder}` tokens remain
- `verify-report.json` shows no placeholder leftovers
- Metrics improvement is documented

## What to report back

```
Method pattern: page.Principal.InputAndSelect({value})
Occurrences: 5 calls across 3 files
Placeholders: {value} — string argument, used inside FillAsync and GetByText
Before: Unsupported actions: 5, TODO comments: 65
After: Unsupported actions: 0, TODO comments: 50
Status: Verified all 5 occurrences correctly parameterized
```
