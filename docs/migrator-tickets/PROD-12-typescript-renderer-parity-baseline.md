# PROD-12 — TypeScript renderer parity baseline

## Goal

Freeze the current Playwright TypeScript target behavior as an explicit parity matrix before further production hardening.

The baseline covers both TypeScript rendering paths:

- legacy `TestFileModel -> PlaywrightTypeScriptRenderer`;
- experimental `MigrationDocument / IR V2 -> PlaywrightTypeScriptIrV2Renderer`.

The purpose is not to claim full TypeScript production readiness. The purpose is to make the current support surface explicit and regression-tested.

## Active TypeScript support covered

The parity matrix asserts active TypeScript output for:

- click/fill/press;
- product waits: visible, hidden, loaded;
- actionability wait elision;
- navigation;
- local declarations;
- locator declarations;
- text assertions: equals, contains, not equals, empty, not empty;
- visibility assertions;
- URL assertions;
- `Assert.AreEqual` shape;
- mapped methods with `Targets.playwright-typescript` overrides;
- table row access/text/count assertions;
- conditional blocks;
- flattened `Assert.Multiple` wrappers.

## Locator coverage

The locator parity matrix covers:

- CSS;
- text;
- test id prefix;
- class prefix;
- raw CSS;
- Playwright locator expressions;
- first/nth/nth-expression selectors.

## TODO-by-design coverage

The baseline also asserts stable TODO diagnostics for current non-goals:

- unmapped method invocation;
- mapped expression assertion;
- unresolved NUnit constraint assertion;
- review-required waits;
- raw statements;
- unsupported source actions.

These cases must not silently disappear or emit unsafe active TypeScript.

## Implementation notes

PROD-12 intentionally keeps the TypeScript target conservative. A case is either rendered as active Playwright TypeScript or as a stable TODO category. This is safer than pretending every source-side helper can be translated without a target-specific profile rule.

The task also fixes small parity gaps found while writing the matrix:

- IR V2 raw locator expressions now wrap CSS/XPath literals as `page.locator(...)`;
- text assertion variants now map to the correct Playwright expectations;
- `Assert.Multiple` is flattened while preserving a source wrapper comment;
- `TableCountKind.CountGreaterThan` maps to `toBeGreaterThan(...)`.
