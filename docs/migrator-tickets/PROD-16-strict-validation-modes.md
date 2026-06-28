# PROD-16 — Strict validation modes

## Goal

Make `config-validate` usable at different hardening stages:

- `warn` — default compatibility mode. Preserves existing behavior and only fails existing structural/dangerous errors.
- `strict` — preparation mode. Surfaces target-specific config migration gaps as warnings.
- `production` — release gate mode. Fails unsafe gaps for the selected target, especially TypeScript mappings that still rely on legacy C#/.NET `TargetStatements`.

## CLI

```bash
dotnet run --project Migrator.Cli -- \
  --mode config-validate \
  --config adapter-config.json \
  --target playwright-typescript \
  --validation-mode production \
  --out config-validate-prod
```

## New checks

### `TARGET_SPECIFIC_STATEMENTS_MISSING`

Emitted in `strict` mode when a mapping has legacy `TargetStatements` but no `Targets.<selected-target>.TargetStatements` override.

### `TS_TARGET_STATEMENTS_REQUIRED`

Emitted as an error in `production` mode for `playwright-typescript` when a mapping still relies on legacy target statements.

### `MAPPED_METHOD_REVIEW_REQUIRED` / `MAPPED_METHOD_REQUIRES_REVIEW`

In `strict`, mappings marked `RequiresReview` are warnings. In `production`, they are errors.

### `PROJECT_VERIFICATION_REQUIRED`

In `production`, the config must include a `Verification` section so generated code can be checked in project context.

## Compatibility

`warn` is the default and preserves existing `config-validate` behavior. Existing adapter-config v1 files continue to validate unless callers explicitly opt into stricter modes.
