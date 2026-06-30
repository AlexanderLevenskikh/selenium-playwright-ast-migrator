# Playbook: Runtime Proof

## Input artifacts

- Generated Playwright files from `migrate` output
- A Playwright .NET test project to host the generated files
- Test environment with auth, data, and services

## Goal

Verify that generated tests run correctly in a real browser environment.

## Steps

1. **Prepare a test host project:**
   - Create or use an existing Playwright .NET project
   - Copy generated `.cs` files into the project
   - Ensure the project references the selected target framework packages (`Microsoft.Playwright.NUnit`/`NUnit` or `Microsoft.Playwright.Xunit`/`xunit`)
2. **Compile the project:**
   ```bash
   dotnet build
   ```
   Fix any compilation errors (missing usings, wrong base class, etc.).
3. **Run the tests:**
   ```bash
   dotnet test --filter "FullyQualifiedName~YourTestClass"
   ```
4. **Classify each failure** using [Classify Failures](classify-failures.md).
5. **For profile-related failures:**
   - Fix the mapping in `adapter-config.json`
   - Re-generate: run `migrate` or `orchestrate`
   - Re-run tests
6. **For environment-related failures:**
   - Fix auth, data, or services
   - Re-run tests
7. **Report results.**

## What NOT to do

- Do NOT delete assertions to make tests pass
- Do NOT invent selectors to fix failing tests
- Do NOT claim runtime proof without an actual browser run
- Do NOT modify generated code to work around environment issues
- Do NOT run tests without the test environment being ready

## Acceptance criteria

- `dotnet build` succeeds with no errors
- `dotnet test` shows specific pass/fail results
- Each failure is classified into a category
- Profile issues are traced back to config gaps

## What to report back

```
Files tested: 3
Tests passed: 5
Tests failed: 2
Failures:
  - Test_SearchByName: wrong locator (page.SearchButton mapped to wrong testId)
  - Test_FilterByDate: test data not available in environment
Next action: Fix SearchButton selector, then re-run
```
