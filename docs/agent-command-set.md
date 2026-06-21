# Autopilot command set

This document lists commands that an agent may use during Autopilot Loop work.

Use actual CLI help/source as final authority.

## Development checks

```bash
dotnet build
dotnet test Migrator.Tests
```

## Migration / verification

```bash
dotnet run --project Migrator.Cli -- --mode analyze --input "<tests>" --config "<config>" --out "analyze-run" --format both

dotnet run --project Migrator.Cli -- --mode migrate --input "<tests>" --config "<config>" --out "migration-run" --format both

dotnet run --project Migrator.Cli -- --mode verify --input "<source Selenium tests>" --config "<config>" --out "verify-run" --format both

dotnet run --project Migrator.Cli -- --mode verify-project --input "<source Selenium tests>" --config "<config>" --out "verify-project-run" --format both
```

## Discovery / bootstrap

```bash
dotnet run --project Migrator.Cli -- --mode doctor --input "<tests>" --config "<config>" --out "doctor" --format both

dotnet run --project Migrator.Cli -- --mode bootstrap-project --input "<tests>" --out "project-bootstrap" --format both

dotnet run --project Migrator.Cli -- --mode index-pom --input "<Selenium project or POM dir>" --out "pom-index" --format both
```

## TODO explanation and prioritization

```bash
dotnet run --project Migrator.Cli -- --mode explain-todo --input "migration/<run>" --out "todo-explanation" --format both

dotnet run --project Migrator.Cli -- --mode smoke-plan --input "migration/<run>" --out "smoke-plan" --format both

dotnet run --project Migrator.Cli -- --mode migration-board --input "migration/<run>" --out "migration-board" --format both
```

## Config safety

```bash
dotnet run --project Migrator.Cli -- --mode config-validate --config "<config>" --out "config-validate"

dotnet run --project Migrator.Cli -- --mode config-diff --before "<old-config>" --after "<new-config>" --out "config-diff"

dotnet run --project Migrator.Cli -- --mode guard --before "migration/<old-run>" --after "migration/<new-run>" --out "guard"
```

## Runtime failure analysis

```bash
dotnet run --project Migrator.Cli -- --mode runtime-classify --input "migration/runtime-logs" --out "runtime-failure-classification" --format both
```

## Workspace rule

Keep generated/report artifacts inside `migration/`.

Use short output names where possible:

```bash
--out "orchestration-7"
```

The CLI should place them under the migration workspace when that behavior is configured.

## Autopilot rule

If a command fails with actionable output, read the output and continue fixing.

Do not stop merely to ask whether to continue.
