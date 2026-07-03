# PROD-07 — Config normalize v1 → v2 command

Status: implemented as a compatibility tool.

## Goal

Make the cross-language profile split inspectable without changing the production migration path.

Legacy `adapter-config.json` remains the runtime source of truth. `config-normalize` converts one or more legacy config layers into a reviewable `migration-profile.v2.json` document plus a report with migration warnings.

## CLI usage

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- \
  --mode config-normalize \
  --config ./adapter-config.base.json \
  --config ./adapter-config.project.json \
  --target playwright-typescript \
  --out config-normalize \
  --format both
```

`--input ./adapter-config.json` is also accepted when a single config file is being normalized.

## Outputs

The command always writes:

```text
migration-profile.v2.json
```

Depending on `--format`, it also writes:

```text
config-normalize-report.json
config-normalize-report.md
```

## External profile shape

The external JSON intentionally uses a flattened source/target shape:

```json
{
  "SchemaVersion": "migration-profile/v2",
  "Source": {
    "Id": "selenium-csharp",
    "Language": "csharp",
    "Framework": "selenium"
  },
  "Target": {
    "Id": "playwright-typescript",
    "Language": "typescript",
    "Framework": "playwright"
  },
  "Project": {}
}
```

This is separate from the internal `MigrationProfile` records so the implementation can evolve without changing the on-disk contract.

## Compatibility rules

- Production migration still reads adapter-config v1.
- The normalized v2 profile includes `LegacyConfig` for lossless review/round-tripping during the transition.
- Legacy `TargetStatements` are preserved, but reported as `CONFIG_V1_LEGACY_TARGET_STATEMENTS` when they are target-ambiguous.
- Target-specific statements under `Targets.<target>.TargetStatements` are preferred before adding more backends.

## Production rule

Do not switch runtime config loading to migration-profile v2 until `config-diff`, `verify`, and real project fixtures prove parity against the legacy adapter-config path.
