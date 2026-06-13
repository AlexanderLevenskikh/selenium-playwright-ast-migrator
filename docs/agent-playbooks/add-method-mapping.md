# Playbook: Add Method Mapping

## Input artifacts

- `unsupported-actions.json` — from analyze or migrate output
- `adapter-config.json` — current configuration
- Source code of the helper method — for understanding semantics

## Goal

Replace an unsupported action with a working Playwright equivalent.

## Steps

1. **Read `unsupported-actions.json`** — find the most frequently occurring unsupported action.
2. **Check occurrence count** — if the method appears 1-2 times, use exact `MethodMapping`. If 3+ times with varying arguments, consider `ParameterizedMethodMapping` instead (see [Add Parameterized Method Mapping](add-parameterized-method-mapping.md)).
3. **Read the helper method source** — understand what the method does:
   - What elements does it interact with?
   - What assertions does it make?
   - What is the expected behavior?
4. **Write the Playwright equivalent** — translate the helper's logic into Playwright C# statements.
5. **Add MethodMapping to config:**
   ```json
   {
     "Methods": [
       {
         "SourceMethod": "page.Loader.ValidateLoading()",
         "TargetStatements": [
           "var loader = Page.Locator(\"[data-test='table-loader']\");",
           "if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();"
         ],
         "RequiresReview": true
       }
     ]
   }
   ```
6. **Re-run orchestrate:**
   ```bash
   dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
   ```
7. **Check generated code** — verify the method call is replaced with the target statements.
8. **Report before/after metrics.**

## What NOT to do

- Do NOT encode project-specific helper names in the tool's recognizers
- Do NOT leave the helper as unmapped if you can write the equivalent
- Do NOT skip `RequiresReview: true` for complex logic
- Do NOT add multiple method mappings in one pass

## Acceptance criteria

- The unsupported action count decreases
- Generated code contains the target statements instead of `// TODO: UNSUPPORTED`
- No syntax errors in generated code
- Metrics improvement is documented

## What to report back

```
Method: page.Loader.ValidateLoading()
Occurrences: 3 files
Target statements: loader visibility check via Playwright
Before: Unsupported actions: 4, TODO comments: 65
After: Unsupported actions: 1, TODO comments: 58
Status: Verified in generated code, RequiresReview=true
```
