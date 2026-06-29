# Extensibility and public API

Migrator is intentionally split into small contracts so external contributors can add source languages, target renderers, or project profiles without hardcoding one team's test framework into the core renderer.

The public preview API is still evolving, but these surfaces are the intended extension points:

| Surface | Status | Purpose |
|---|---|---|
| `ISourceFrontend` | Stable public API for built-in/contributed frontends | Parse one source language/framework and lower it into Migrator IR. |
| `ITargetBackend` | Stable public API for built-in/contributed backends | Render Migrator IR or legacy test models into a target test framework. |
| `adapter-config/v1` | Stable public profile format | Store project-specific selectors, helper mappings, waits, table/list mappings, and validation settings. |
| `migration-profile/v2` | Experimental normalized profile format | Separates source, target, and project sections for future multi-language profiles. |
| CLI command catalog | Stable public UX contract | Keeps stable, experimental, and internal commands visibly separated. |

Dynamic plugin loading from arbitrary external assemblies is **not** supported yet. Today, a new frontend/backend is added in-process: implement the contract, register it in the built-in registry or a host application, and add capability diagnostics/tests. The contracts are shaped so dynamic loading can be added later without changing users' profile files.

## Discover available capabilities

Run:

```bash
selenium-pw-migrator --mode capabilities --out capabilities --format both
```

This writes:

```text
capabilities/
  capabilities-report.json
  capabilities-report.md
```

Source-processing modes such as `analyze`, `migrate`, `verify`, `doctor`, and `orchestrate` also write:

```text
source-capabilities-report.json/md
target-capabilities-report.json/md
```

These reports are part of the public extension story: a frontend or backend must be honest about whether it is stable, experimental, limited, or unsupported in each area.

## Add a new source frontend

1. Implement `ISourceFrontend`.
2. Define a stable `SourceSpec` id such as `selenium-ruby`.
3. Provide user aliases such as `ruby-selenium` or `rb`.
4. Return a `SourceCapabilityReport` from `SourceCapabilityCatalog` or a custom report.
5. Parse source files into `MigrationDocument` IR V2, or wrap an existing legacy `ITestFileParser` through `TestFileParserSourceFrontend`.
6. Register it in `SourceFrontendRegistry`.
7. Add fixture tests and CLI smoke tests.

See [Source frontend contract](source-frontend-contract.md).

## Add a new target backend

1. Implement `ITargetBackend`.
2. Define a stable `TargetSpec` id such as `playwright-python`.
3. Provide user aliases such as `python` or `py`.
4. Return a `TargetCapabilityReport` from `TargetCapabilityCatalog` or a custom report.
5. Render `TestFileModel` for legacy compatibility and `MigrationDocument` for IR V2.
6. Register it in `TargetBackendRegistry`.
7. Add renderer parity tests, generated-file naming tests, and verification docs.

See [Target backend contract](target-backend-contract.md).

## Extend project-specific behavior

Most migrations do **not** need a new frontend/backend. Prefer `adapter-config/v1` when the behavior is project-specific:

- selectors and PageObject targets;
- helper methods and parameterized method mappings;
- table/list/pagination mappings;
- wait policies;
- source-only identifiers;
- target-specific statements under `Targets.<target>`;
- quality gates and verification settings.

See [Config and profile guide](config-profile-guide.md) and [Adapter-config versioning](adapter-config-versioning.md).

## API stability rules

Stable public API means:

- ids are stable and used in reports/config: `selenium-csharp`, `playwright-dotnet`, `adapter-config/v1`;
- aliases are user convenience only and may be expanded without changing reports;
- capability report schema versions are explicit: `source-capabilities/v1`, `target-capabilities/v1`, `migrator-capabilities/v1`;
- experimental features must say so in capability reports and docs;
- internal commands/classes should not be required by external migration profiles.

When changing a public contract, update this page, capability reports, schema docs, and regression tests in the same PR.
