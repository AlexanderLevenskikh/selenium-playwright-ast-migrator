# PROD-05 — DotNet renderer IR V2 experimental path

## Goal

Keep the existing Playwright .NET renderer production-safe while adding an opt-in path that renders through the cross-language `MigrationDocument` IR V2 contract.

The default path remains legacy:

```text
TestFileModel -> PlaywrightDotNetRenderer
```

The experimental path is:

```text
TestFileModel -> MigrationDocument -> ITargetBackend.RenderDocument -> Playwright .NET output
```

During the transition `PlaywrightDotNetBackend.RenderDocument` lowers IR V2 through `LegacyIrBridge` so output parity can be proven before renderer internals move to direct IR rendering.

## CLI usage

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- \
  --mode migrate \
  --input ./OldTests \
  --config ./adapter-config.json \
  --target dotnet \
  --render-ir v2 \
  --out generated-v2
```

`--render-ir legacy` is the default.

`--render-ir v2` is intentionally not enabled for `orchestrate` yet. Use `migrate` or `verify` first so V2 parity failures stay easy to isolate.

## Safety checks

`DotNetRendererV2ExperimentalPathTests` verifies that:

- `PlaywrightDotNetBackend.RenderDocument(MigrationDocument)` matches legacy `Render(TestFileModel)` output.
- `MigrationPipelineRenderMode.IrV2` matches `MigrationPipelineRenderMode.Legacy` output and report counters.
- Legacy `IRenderer` construction still works and cannot accidentally opt into IR V2.

## Production rule

Do not switch the default renderer path to IR V2 until golden fixtures prove parity for the real migration corpus.
