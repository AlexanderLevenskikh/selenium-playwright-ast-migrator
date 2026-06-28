# PROD-11 — LegacyIrBridge parity matrix

## Goal

Make the transitional `LegacyIrBridge` safe enough for IR V2 adoption by proving that supported legacy actions survive:

```text
TestFileModel/TestAction → MigrationDocument/IR V2 → TestFileModel/TestAction
```

without silently degrading to `UnsupportedAction` or losing renderer-critical fields.

## What is covered

`LegacyIrBridgeParityMatrixTests` covers the current supported legacy action matrix:

- `ClickAction`
- `SendKeysAction`
- `PressAction`
- `WaitForAction`
- `TextAssertionAction`
- `VisibilityAssertionAction`
- `UrlAssertionAction`
- `NavigationAction`
- `LocalDeclarationAction`
- `LocatorDeclarationAction`
- `MethodInvocationAction`
- `MappedMethodInvocationAction`
- `MappedExpressionAssertionAction`
- `AssertAreEqualAction`
- `AssertThatAction`
- `AssertMultipleAction`
- `TableCountAssertionAction`
- `TableRowAccessAction`
- `TableRowTextAccessAction`
- `ConditionalBlockAction`
- `RawStatementAction`
- intentional `UnsupportedAction`

It also covers target expression parity for:

- CSS selectors
- text locators
- test-id prefix locators
- class-name prefix locators
- page-object properties
- raw locator expressions
- Playwright locator expressions
- unresolved locators
- `First` / `Nth` match strategies
- dynamic `NthIndexExpression`

## Bridge hardening included

The task also fixes parity gaps found while adding the matrix:

1. `ClassFields` are preserved when lowering IR V2 back to `TestFileModel`.
2. `TargetKind.ClassNameBeginning` gets a first-class IR locator node: `ByClassNamePrefix`.
3. `TargetKind.PlaywrightLocator` gets a transitional IR locator node: `PlaywrightLocatorRef`.
4. Locator match metadata now preserves dynamic nth expressions through IR V2.
5. `NavigationAction.SourceText` is preserved through `UrlNavigationIntent`.

## Validation

Recommended focused test run:

```bash
dotnet test Migrator.Tests/Migrator.Tests.csproj --filter "LegacyIrBridgeParityMatrixTests|LegacyIrBridgeGoldenTests|CanonicalIrV2ActionModelTests" --nologo
```

Recommended full validation:

```bash
dotnet test Migrator.sln --nologo
```
