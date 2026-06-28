# PROD-06 — TypeScript renderer IR V2 experimental path

Status: implemented as an opt-in experimental rendering path.

## Goal

Make Playwright TypeScript capable of rendering from `MigrationDocument` directly, without making IR V2 the production default yet.

This mirrors PROD-05 for Playwright .NET, but TS is allowed to become cleaner earlier because it is the newer target backend.

## What changed

- Added `PlaywrightTypeScriptIrV2Renderer`.
- `PlaywrightTypeScriptBackend.RenderDocument(MigrationDocument)` now uses the direct IR V2 renderer.
- Legacy `PlaywrightTypeScriptRenderer.Render(TestFileModel)` remains unchanged and remains the default production path.
- `MigrationPipelineRenderMode.IrV2` can exercise the TS IR V2 path through the target backend.

## Current supported IR V2 surface

The direct renderer supports the current TypeScript renderer's main safe surface:

- click
- fill
- press
- waits for visible/hidden/default waitFor
- navigation
- local declarations
- locator declarations
- mapped method statements, including target-specific `playwright-typescript` statements and `{TARGET}` / `{result}` placeholders
- table row access
- table row text access
- table count assertions
- semantic text/visibility/url assertions
- raw/unsupported statements as TODOs

## Compatibility strategy

Production stays on legacy rendering unless the caller opts into:

```bash
--render-ir v2
```

The parity tests compare:

```text
TestFileModel -> PlaywrightTypeScriptRenderer
```

against:

```text
TestFileModel -> LegacyIrBridge -> MigrationDocument -> PlaywrightTypeScriptIrV2Renderer
```

for the action surface already supported by the legacy TS renderer.

## Why this matters

This moves TypeScript toward a real target backend, instead of a renderer that depends on C#-shaped legacy actions forever. Once the IR V2 path becomes broad enough, TS can become the first backend to stop depending on legacy lowering.
