# PROD-08 — SourceFrontend CLI selection

## Goal

Make the source side explicit in the CLI so the migration engine can evolve from a hard-coded C# Selenium parser into a source/frontend registry.

This is a production-hardening step, not a full Java/Python production implementation.

## User-facing CLI

Default remains backward-compatible:

```bash
Migrator.Cli --mode migrate --input ./OldTests --target dotnet
```

Equivalent explicit source:

```bash
Migrator.Cli --mode migrate \
  --source csharp-selenium \
  --input ./OldTests \
  --target dotnet
```

Experimental Java Selenium source:

```bash
Migrator.Cli --mode migrate \
  --source java-selenium \
  --input ./JavaTests \
  --target ts \
  --out generated-java-ts
```

Reserved Python Selenium source id:

```bash
Migrator.Cli --mode migrate \
  --source python-selenium \
  --input ./PythonTests \
  --target ts
```

Python currently fails with an explicit not-implemented message. It is intentionally registered as a reserved source id for the PROD-10 spike so configs/docs can refer to the future source without pretending it is production-ready.

## Supported sources in this step

| CLI value | Stable source id | Status |
|---|---|---|
| `csharp-selenium` | `selenium-csharp` | production/default |
| `java-selenium` | `selenium-java` | experimental spike |
| `python-selenium` | `selenium-python` | reserved for PROD-10 |

Aliases are accepted for convenience, but reports and profile output use stable ids.

## Internal changes

- Added `--source` parsing to CLI.
- Added built-in `SourceFrontendRegistry` composition in CLI.
- Kept `csharp-selenium` as default.
- Routed parser selection through resolved source frontend.
- `config-normalize` now writes the selected `SourceSpec` into `migration-profile.v2.json`.
- `dump-ir --ir-version v2` now writes the selected `SourceSpec` into the V2 dump.
- Added `UnsupportedSourceFrontend` to reserve future source ids with explicit failure messages.
- `MigrationPipeline` now accepts an optional `SourceSpec` for IR V2 bridge metadata.

## Acceptance criteria

- Existing commands without `--source` behave as before.
- `--source csharp-selenium` behaves like the old default.
- `--source java-selenium` can run the experimental Java parser for simple Selenium Java fixtures.
- `--source python-selenium` fails clearly and does not silently fall back to C#.
- V2 profile/dump artifacts include the selected source metadata.

## Next steps

PROD-09 should expand Java Selenium from parser spike to MVP fixture coverage. PROD-10 should replace the reserved Python frontend with an experimental parser spike.
