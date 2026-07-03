# PROD-15 — config-diff v1/v2 semantic parity

## Goal

`config-normalize` introduces `migration-profile/v2`, but teams still need a safe way to prove that a normalized profile is behaviorally equivalent to the legacy `adapter-config` before treating the v2 profile as source of truth.

## Changes

- `--mode config-diff` now accepts both legacy `adapter-config` v1 and `migration-profile/v2` documents on either side of `--before` / `--after`.
- When a v2 profile contains `LegacyConfig`, config-diff compares that lossless embedded legacy config.
- When a v2 profile omits `LegacyConfig`, config-diff reconstructs a comparable `ProjectAdapterConfig` from `Source`, `Target`, and `Project` sections.
- The report summary now includes:
  - input kinds, for example `adapter-config/v1 → migration-profile/v2`;
  - `SemanticParity: passed|changed`;
  - section counts for source, target, and project mappings.

## Usage

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- \
  --mode config-normalize \
  --config adapter-config.json \
  --target playwright-typescript \
  --out config-normalized \
  --format both

dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- \
  --mode config-diff \
  --before adapter-config.json \
  --after config-normalized/migration-profile.v2.json \
  --out config-diff-v2 \
  --format both
```

## Production rule

A normalized profile should not replace the v1 config until config-diff reports:

- `SemanticParity: passed`
- `Changes: 0`
- `Risks: 0`

Differences are allowed only when they are intentional and reviewed.
