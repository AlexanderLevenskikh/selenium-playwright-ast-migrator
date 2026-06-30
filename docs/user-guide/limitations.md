# Limitations

The Migrator is a migration accelerator, not a fully automatic replacement for engineering review. This page lists the boundaries that matter for public preview users.

## Not 100% automatic

The tool generates scaffolding and reports. Project-specific semantics still require reviewable profile/config mappings: helper methods, navigation, auth setup, table interactions, product-state waits, and assertion helpers.

Good output depends on good source truth.

## Profile config is required for production-quality output

Without a trusted `adapter-config.json`, many page elements remain unresolved and generated code contains TODO locators:

```csharp
await Page.Locator("TODO: page.User").ClickAsync();
```

This is useful for estimating migration scope, but it is not production-ready. Add mappings from verified PageObject selectors, target Playwright tests/POMs, actual HTML attributes, or reviewed helper semantics.

## Runtime pass requires environment, auth, and test data

The tool can generate code and perform static/project-aware verification. It does not magically provide:

- test environments;
- authentication and authorization setup;
- required test data;
- backend state or mocks;
- browser-runtime proof for your product.

Runtime verification remains the responsibility of the development team.

## Stable and preview paths differ

| Capability | Status | Limitation |
|---|---|---|
| Selenium C# / NUnit or xUnit → Playwright .NET / NUnit or xUnit | Stable public path | Best-covered path; NUnit remains the default target framework; still requires project profiles. |
| Selenium C# / NUnit or xUnit → Playwright TypeScript | Experimental preview | Generated `.spec.ts` files may require TS-specific profile overrides. Project-aware verification requires `--ts-project`. |
| Selenium Java source | Experimental MVP | Handles common Java Selenium fixtures without Java semantic analysis. |
| Selenium Python source | Experimental spike | Handles simple pytest/unittest Selenium patterns; not production-ready. |

The TypeScript target is supported as an experimental target, not as the primary stable path. Use `migrate --target ts` and `verify-ts-project --ts-project <path>` for TypeScript verification.

## Scaffold output is compile-only, not runtime-ready

`scaffold` mode generates a minimal Playwright .NET project structure with stub authentication and placeholder routes. The scaffold:

- compiles as a starting point;
- does not guarantee runtime pass;
- uses placeholder routes such as `<test-login>` and `<ROUTE_SOURCE_TRUTH_REQUIRED>`;
- requires you to implement `LoginAsync`, set `E2E_BASE_URL`, and review `adapter-config.draft.json`.

## Discovery and proposals require review

`discover-target` and `propose` produce draft artifacts. They do not prove source truth.

Draft markers such as `<REVIEW_REQUIRED>`, `<SOURCE_TRUTH_REQUIRED>`, and `<redacted-host>` must be reviewed before the output is used in production profiles.

## Complex table, list, and pagination flows may need manual migration

The tool handles some common row access patterns and table/list mappings. Manual migration or deeper profile work may still be required for:

- pagination across multiple pages;
- sorting, filtering, or searching within tables;
- nested or hierarchical tables;
- drag-and-drop or row reordering;
- dynamic row identity that cannot be expressed as a stable locator.

## Some generated tests need body-level edits

The Migrator translates many individual actions, but it may not preserve complex test-level logic perfectly. Manual edits may be needed for:

- conditional logic that depends on runtime page state;
- multi-element assertions;
- data-driven tests with project-specific dynamic parameters;
- custom assertion helpers;
- direct JavaScript execution;
- iframe switching with dynamic frame locators;
- file upload/download flows with custom handling.

## Unsupported actions are preserved as TODOs

Some Selenium patterns are recognized but intentionally left as TODO comments:

- custom WebDriver extensions outside standard Selenium API;
- project-specific PageObject frameworks with unconventional patterns;
- assertion libraries beyond the covered NUnit/FluentAssertions subset;
- helpers whose body or semantics cannot be proven safely.

The generated TODO keeps the source expression and line information whenever possible. Losing behavior silently is worse than preserving an explicit TODO.

## Verification levels are different

- `verify` checks generated output quality without compiling a full target project.
- `verify-project` compiles generated Playwright .NET tests in a project-aware harness.
- `verify-ts-project` type-checks generated TypeScript specs against an existing Playwright TS project.
- None of these replace a real browser runtime smoke run against your product.

## Scope limitations

- Scoping is per file, not per test/method.
- Multiple scopes matching one file use first-match-wins with a warning.
- There is no inheritance between scopes.
- Source path matching is intentionally simple and conservative.

## Semantic vs syntax fallback

For Selenium C#, the tool prefers Roslyn semantic information. Files that cannot be resolved with proper references may fall back to syntax-only recognition, which is less precise. Reports include confidence information so users can prioritize review.


## Extensibility is in-process in public preview

`ISourceFrontend` and `ITargetBackend` are public extension contracts, but dynamic plugin loading from arbitrary external assemblies is not supported yet. New frontends/backends should currently be contributed as built-in implementations or hosted by an embedding application. Use `--mode capabilities` to inspect the built-in support matrix.

## Public preview caveat

Stable commands are intended for external users. Experimental commands may change between preview releases. Review generated code, reports, and profile diffs before using output as production test code.
