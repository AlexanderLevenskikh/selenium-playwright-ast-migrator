# Playbook: Classify Failures

## Input artifacts

- Test failure output from `dotnet test`
- Generated Playwright files
- `adapter-config.json`
- `verify-report.json`

## Goal

Categorize each test failure to determine the correct remediation path.

## Failure taxonomy

| Category | Cause | How to identify | Who fixes |
|---|---|---|---|
| **Wrong locator** | Target expression doesn't match real page element | Playwright throws `Element not found` or `Strict mode violation` | Fix UiTarget mapping in config |
| **Strict mode** | Multiple elements match the same locator | Playwright error: "strict mode violation" | Add `Match: "First"` or `Match: "Nth"` |
| **Helper semantics** | Project helper behavior not captured by mapping | Generated code runs but produces wrong behavior | Add or fix MethodMapping / ParameterizedMethodMapping |
| **Loader/wait** | Page loading logic not translated correctly | Timeout or stale element errors | Add wait logic to MethodMapping |
| **Table/list assertion** | Row access or assertion pattern differs | Wrong row selected, or assertion on wrong element | Configure table/list mappings or manual edit |
| **Test data** | Required data not present in test environment | Test fails with data not found, empty results | Data setup (outside Migrator scope) |
| **Environment/backend** | Auth, network, or service issue | HTTP errors, auth failures, timeouts | Infrastructure (outside Migrator scope) |
| **Generated code bug** | The Migrator produced incorrect C# | Compilation error or logic error in generated code | Tool fix or manual edit |
| **Profile issue** | Missing or wrong config mapping | TODO comments, unmapped targets in generated code | Add/fix adapter-config.json |
| **Manual migration required** | Complex logic that cannot be auto-mapped | Unsupported action that has no equivalent | Developer writes by hand |

## Steps

1. **Read the test failure output** — capture the error message and stack trace.
2. **Match the error to a category** using the table above.
3. **For profile-related categories** (wrong locator, strict mode, helper semantics, profile issue):
   - Follow the relevant playbook to fix the config
   - Re-generate and re-run
4. **For environment-related categories** (test data, environment/backend):
   - Document the blocker
   - Fix environment, re-run
5. **For generated code bugs:**
   - Report the issue with the specific file and line
   - Apply manual fix in generated code
6. **For manual migration required:**
   - Mark the test as requiring manual effort
   - Estimate complexity

## What NOT to do

- Do NOT misclassify environment issues as tool bugs
- Do NOT claim a fix without re-running the test
- Do NOT merge multiple failure categories into one

## What to report back

```
Failure: Test_FilterByDate
Category: Test data
Evidence: "No records found for date range"
Action: Data setup needed, outside Migrator scope

Failure: Test_SearchByName
Category: Wrong locator
Evidence: "Element with test-id 't_search_btn' not found"
Action: Fix UiTarget mapping for page.SearchButton
```
