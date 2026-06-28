# PROD-03 — IR V2 dump mode

## Goal

Expose `MigrationDocument` / IR V2 as a stable diagnostic artifact before renderers are switched to IR V2 directly.

This gives refactors two independent baselines:

- legacy `TestFileModel/TestAction` dump for current behavior;
- IR V2 `MigrationDocument` dump for the future source/target-neutral contract.

## CLI

Default behavior remains backward-compatible:

```bash
dotnet run --project Migrator.Cli -- \
  --mode dump-ir \
  --input ./OldTests \
  --config ./adapter-config.json \
  --out ir-dump \
  --format both
```

Writes:

```text
ir-dump.json
ir-dump.md
```

To dump only IR V2:

```bash
dotnet run --project Migrator.Cli -- \
  --mode dump-ir \
  --input ./OldTests \
  --config ./adapter-config.json \
  --out ir-dump \
  --ir-version v2 \
  --format both
```

To dump both schemas side-by-side:

```bash
dotnet run --project Migrator.Cli -- \
  --mode dump-ir \
  --input ./OldTests \
  --config ./adapter-config.json \
  --out ir-dump \
  --ir-version both \
  --format both
```

Writes:

```text
ir-dump.legacy.json
ir-dump.legacy.md
ir-dump.v2.json
ir-dump.v2.md
```

## Notes

`V2IrDumpWriter` intentionally serializes stable dump DTOs instead of serializing `MigrationDocument` directly. This avoids fragile polymorphic JSON and lets IR node internals evolve without breaking every snapshot for incidental reasons.

## Follow-up

After this lands, the next hardening step is to add golden snapshots for representative `ir-dump.v2.json` outputs and then start filling IR V2 canonical actions beyond the current legacy bridge mapping.
