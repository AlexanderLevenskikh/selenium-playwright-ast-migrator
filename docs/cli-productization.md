# CLI Productization

The CLI is now backed by a single command catalog in `Migrator.Cli/Commands/CliCommandCatalog.cs`.

The catalog is the source of truth for:

- valid `--mode` values;
- command groups: stable public, experimental preview, internal/maintainer;
- default output directories;
- whether `--input` is required;
- top-level input preflight behavior;
- global and command-specific help text.

## Help

Global help:

```bash
selenium-pw-migrator --help
```

Command help:

```bash
selenium-pw-migrator --mode migrate --help
selenium-pw-migrator --mode verify-project --help
selenium-pw-migrator --mode helper-inventory --help
```

Help requests return exit code `0` and do not run migration logic.

## Command groups

### Stable public commands

Stable commands are the recommended public workflow surface: `doctor`, `migrate`, `verify-project`, `config-validate`, `index-pom`, `helper-inventory`, `propose`, `guard`, `scaffold`, `capabilities`, and related support commands.

### Experimental preview commands

Experimental commands are available but may change shape before a stable release. Examples: `verify-ts-project`, `explain-todo`, `smoke-plan`, `runtime-classify`, `migration-board`, `profile-match`, and `orchestrate`.

### Internal/maintainer commands

Internal commands are diagnostic or compatibility-oriented commands that should stay available for maintainers but should not be presented as the default public workflow. Examples: `dump-ir` and `config-normalize`.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Success or help shown. |
| 1 | User/input error or verification/quality-gate failure. |
| 2 | Invalid config, invalid source/target/mode, unsupported gate, or preflight failure. |
| 3 | TODO quality gate or stage failure. |
| 4 | Generated syntax errors detected. |

## Adding a command

Prefer a dedicated command class under `Migrator.Cli/Commands/` for new self-contained features. Add one catalog entry with the command name, group, default output folder, input requirements, and examples. The `Program.cs` router should stay thin and should not gain another large command implementation block.

After adding or changing a command, update or add tests in `CliProductizationTests` so mode validation, help, grouping, and default-output behavior stay catalog-driven.
