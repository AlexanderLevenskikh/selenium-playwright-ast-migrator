# PROD-14. TypeScript mapped helper support hardening

## Goal

Make `Targets.playwright-typescript.TargetStatements` a safe production bridge for project-specific helper mappings.

The TypeScript renderer has two execution paths:

- legacy `TestFileModel -> PlaywrightTypeScriptRenderer`;
- IR V2 `MigrationDocument -> PlaywrightTypeScriptIrV2Renderer`.

Both paths must render mapped helper statements consistently and must not emit invalid or review-required TypeScript as active code.

## What changed

- Target-specific statement lookup now accepts the stable target id and aliases:
  - `playwright-typescript`
  - `ts`
  - `typescript`
  - `pw-ts`
  - `playwright-ts`
- `{TARGET}` is substituted only when the mapped receiver has a resolved target.
- `{result}` is substituted only when the source invocation produced a result variable.
- Remaining placeholders such as `{value}` or `{expected}` are detected and rendered as `MIGRATOR:UNRESOLVED_PLACEHOLDER` instead of leaking into active TS.
- `RequiresReview` mappings render a review TODO and comment out prepared target statements instead of emitting active TypeScript.
- Empty mapped helpers now produce `MIGRATOR:TS_MAPPING_REQUIRED` instead of silently rendering nothing.
- Legacy Playwright .NET target statements can still be translated when no TS-specific override exists.

## Safety rules

A mapped statement is emitted as active TypeScript only when all of the following are true:

1. the selected mapping is not review-required;
2. all placeholders are resolved;
3. the statement is either a TS-specific override or recognized as TypeScript-safe;
4. otherwise a known Playwright .NET statement can be translated to TypeScript.

If any of these checks fail, the renderer emits a stable TODO with a `MIGRATOR:*` code.

## Tests

Covered by:

- `TypeScriptMappedHelperSupportTests`
- existing `TypeScriptRendererParityBaselineTests`
- existing `TypeScriptRendererV2ExperimentalPathTests`
