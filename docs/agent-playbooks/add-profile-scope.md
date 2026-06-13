# Playbook: Add Profile Scope

## Input artifacts

- `adapter-config.json` — current configuration
- `unmapped-targets.json` — from analyze output
- `verify-report.json` — from verify output

## Goal

Configure file-specific overrides for a test suite or individual file that needs different settings from the global config.

## Steps

1. **Identify the need for a scope:**
   - A specific file needs different `SetUpStatements` (different navigation)
   - A specific file uses different page elements not covered by global UiTargets
   - A specific file needs a different `TestHost` base class
2. **Create the scope entry:**
   ```json
   {
     "Scopes": [
       {
         "Name": "CatalogPrincipals",
         "SourcePathPatterns": ["**/CatalogPrincipalsFilter.cs"],
         "TestHost": {
           "BaseClass": "TestBase",
           "SetUpStatements": [
             "await Page.GotoAsync(\"<test-login>\");",
             "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
           ]
         },
         "UiTargets": [
           {
             "SourceExpression": "page.Table",
             "RowTarget": {
               "TargetExpression": "t_table_row_item",
               "TargetKind": "TestId",
               "TestIdAttribute": "data-test"
             }
           }
         ]
       }
     ]
   }
   ```
3. **Verify scope matching** — the `SourcePathPatterns` should match only the intended files.
4. **Re-run orchestrate:**
   ```bash
   dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
   ```
5. **Check for scope warnings** — `verify-report.json` should not show `FailOnMultipleMatchingScopes` warnings.
6. **Verify the generated code** — confirm the file uses the scope's settings.
7. **Report before/after metrics.**

## What NOT to do

- Do NOT create overlapping scopes without understanding first-match-wins behavior
- Do NOT use scopes for settings that should be global
- Do NOT add project-specific logic to Core/Roslyn/Renderer
- Do NOT use absolute paths in `SourcePathPatterns`

## Acceptance criteria

- The scoped file uses the correct settings
- No scope conflict warnings in verify report
- Generated code for the scoped file matches expectations
- Other files are not affected by the scope

## What to report back

```
Scope name: CatalogPrincipals
Matching files: CatalogPrincipalsFilter.cs
Overrides: TestHost (SetUpStatements), UiTargets (Table RowTarget)
Before: TODO comments: 65, unmapped: 17
After: TODO comments: 55, unmapped: 12
Status: Scope applied correctly, no conflicts
```
