# Playbook: Add Table/List Mapping

## Input artifacts

- `unmapped-targets.json` — from analyze output
- `adapter-config.json` — current configuration
- PageObject source for table/list implementation

## Goal

Map table or list elements so that row access patterns generate correct Playwright code.

## Steps

1. **Identify table/list patterns** — in `unmapped-targets.json`, look for:
   - `page.Table.Items.ElementAt(N)` — indexed row access
   - `page.Table.Rows` — row collection
   - `page.Table.Headers` — column headers
2. **Locate the row selector in source truth** — find the PageObject's row locator:
   - `WithDataTestId("t_table_row_item")` → row uses `data-testid`
   - `WithDataTest("t_table_row_item")` → row uses `data-test`
3. **Add RowTarget mapping:**
   ```json
   {
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
   ```
4. **Add column header mappings if needed:**
   ```json
   {
     "UiTargets": [
       {
         "SourceExpression": "page.Table.Headers.Name",
         "TargetExpression": "Name",
         "TargetKind": "Text"
       }
     ]
   }
   ```
5. **Re-run orchestrate:**
   ```bash
   dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
   ```
6. **Verify generated code** — check that `.ElementAt(N)` patterns produce correct Playwright code.
7. **Report before/after metrics.**

## What NOT to do

- Do NOT invent row selectors — verify against source truth
- Do NOT assume all tables use the same row selector
- Do NOT skip manual review for complex table interactions (pagination, sorting, filtering)
- Do NOT try to map nested tables with a single RowTarget

## Acceptance criteria

- Table row access generates correct Playwright code
- No TODO comments for table-related locators
- `verify-report.json` shows no issues for table elements
- Metrics improvement is documented

## What to report back

```
Table element: page.Table
Row selector source: TablePage.cs:28 — WithDataTest("t_table_row_item")
RowTarget: TestId, data-test attribute
Mapped patterns: ElementAt(0) through ElementAt(4)
Before: Unmapped: 17, TODO comments: 65
After: Unmapped: 10, TODO comments: 52
Status: Simple row access verified. Complex patterns need manual review.
```
