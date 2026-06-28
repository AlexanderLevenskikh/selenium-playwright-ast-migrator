# PROD-04 — IR V2 canonical action model

## Goal

Make IR V2 more than a thin legacy dump by adding canonical statement nodes for the common Selenium migration actions that already exist in the legacy model.

This is still a compatibility step: renderers keep using the legacy executable model by default, while `LegacyIrBridge` can now preserve richer action shape in `MigrationDocument`.

## Added canonical statements

IR V2 now has explicit nodes for:

- `ClickStatementIr`
- `FillStatementIr`
- `PressStatementIr`
- `DeclarationStatementIr`
- `LocatorDeclarationStatementIr`
- `PageObjectFieldStatementIr`
- `MethodInvocationStatementIr`
- `MappedMethodStatementIr`
- `MappedExpressionAssertionStatementIr`
- `AssertAreEqualStatementIr`
- `AssertThatStatementIr`
- `AssertMultipleStatementIr`
- `TableCountAssertionStatementIr`
- `TableRowAccessStatementIr`
- `TableRowTextAccessStatementIr`
- `ConditionalBlockStatementIr`
- `AssertionStatementIr`
- `WaitStatementIr`
- `NavigationStatementIr`
- `RawStatementIr`
- `UnsupportedStatementIr`

## Compatibility contract

`LegacyIrBridge` supports both directions for the supported nodes:

```text
TestFileModel → MigrationDocument → TestFileModel
```

The goal is to avoid losing executable legacy shape while renderers gradually move to IR V2.

## Dump support

`V2IrDumpWriter` now emits stable dump kinds for the new canonical statements, so `--mode dump-ir --ir-version v2` can show richer parser/adapter intent before the renderer is switched.

## Tests

Added `CanonicalIrV2ActionModelTests` for:

- legacy action → canonical IR node mapping;
- canonical IR → legacy action roundtrip;
- V2 dump action kind coverage.

## Non-goals

This ticket does not switch DotNet/TypeScript renderers to read IR V2 directly. That is intentionally left for PROD-05/PROD-06 behind an experimental path.
