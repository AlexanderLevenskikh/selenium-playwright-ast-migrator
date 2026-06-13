# Limitations

What the Migrator can and cannot do.

## Not 100% automatic

The Migrator generates scaffolding, not production-ready tests. Project-specific semantics (helper methods, navigation logic, table interactions) require configuration in the adapter profile. The quality of output depends on the quality of the profile config.

## Profile config is required for good results

Without an `adapter-config.json`, the tool generates `TODO` locators for all page elements:

```csharp
await Page.Locator("TODO: page.User").ClickAsync();
```

This is useful for estimating migration scope, but not for production. Good output requires source-truth mappings for your project's PageObjects and helpers.

## Runtime pass requires environment, auth, and test data

The tool generates code. It cannot:
- Set up test environments
- Configure authentication or authorization
- Create or manage test data
- Run browser tests

Runtime verification is the responsibility of the development team.

## Discovery output requires review

`discover-target` mode scans an existing Playwright project and produces a draft config. This draft contains:
- `<REVIEW_REQUIRED>` — values that need manual verification
- `<SOURCE_TRUTH_REQUIRED>` — selectors that must be verified against source code
- `<redacted-host>` — redacted URLs and credentials

The draft is a starting point, not a final config.

## Propose mode does not know source truth

`propose` mode analyzes migration artifacts and suggests config improvements. However, it cannot verify selectors against your PageObject source. Proposed UiTarget mappings use `<SOURCE_TRUTH_REQUIRED>` placeholders that you must fill in.

## Complex table and pagination flows may need manual migration

The tool handles simple row access patterns (`.ElementAt(N)`) via `RowTarget` mappings. Complex patterns like:
- Pagination across multiple pages
- Sorting, filtering, or searching within tables
- Nested tables or hierarchical data
- Drag-and-drop or row reordering

...may require manual migration or extensive body-level edits.

## Some generated tests need body-level edits

The Migrator translates individual actions but may not preserve test-level logic perfectly. Cases requiring manual edits:
- Conditional logic that depends on page state
- Complex assertions spanning multiple elements
- Data-driven tests with dynamic parameters
- Tests that use project-specific assertion helpers

## Playwright TypeScript is not supported

The tool generates C# code for Playwright .NET only. It does not support:
- Playwright for TypeScript/JavaScript
- Playwright for Java
- Playwright for Python

## Selenium patterns not covered

Some Selenium patterns are recognized as `Unsupported` and left as TODO comments:
- Custom WebDriver extensions not in standard Selenium API
- Project-specific page object frameworks with unconventional patterns
- Non-standard assertion libraries beyond FluentAssertions
- Direct JavaScript execution (`IJavaScriptExecutor.ExecuteScript`)
- File uploads/downloads with custom handling
- iframe switching with dynamic frame locators

## No runtime execution or auto-apply

The tool:
- Does not run tests in a browser
- Does not modify `adapter-config.json` automatically
- Does not auto-apply proposals to your config
- Does not call external AI services

All decisions require human review and approval.

## Scope limitations

- Scoping is per-file, not per-test or per-method
- Multiple scopes matching one file use first-match-wins (with a warning)
- No inheritance between scopes
- No complex glob patterns in `SourcePathPatterns` (only exact filename and suffix match)

## Semantic vs Syntax fallback

The tool uses two recognition strategies:
- **Semantic**: full Roslyn type information (more accurate)
- **Syntax fallback**: AST-based recognition without SemanticModel (less accurate)

Files without proper references may fall back to syntax-only recognition, producing less precise results.

## This is an MVP

The current release covers the core pipeline and common patterns. Future improvements may address:
- Template method mappings for deferred helpers
- More sophisticated table/list strategies
- Automated selector verification
- Integration with CI/CD pipelines
