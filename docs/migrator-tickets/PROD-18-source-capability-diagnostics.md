# PROD-18 — Source capability diagnostics

Status: implemented

## Goal

Make source frontend maturity explicit. Cross-language migration should not make experimental Java/Python frontends look equivalent to the C# Roslyn-backed frontend.

## Added

- `SourceCapabilityReport` and `SourceCapabilityItem` in `Migrator.Core.SourceFrontends`.
- `ISourceFrontend.Capabilities`.
- `SourceCapabilityCatalog` with built-in profiles for:
  - `selenium-csharp` — `stable`
  - `selenium-java` — `experimental-mvp`
  - `selenium-python` — `experimental-spike`
- CLI artifact output for source-processing modes:
  - `source-capabilities-report.json`
  - `source-capabilities-report.md`

## Capability areas

Each source reports support for:

- semantic model
- test frameworks
- Selenium actions
- locators
- waits
- assertions
- page objects
- target config / helper mappings

Support values are intentionally coarse and stable:

- `strong`
- `basic`
- `limited`
- `none`

## CLI behavior

For source-processing modes such as `migrate`, `dump-ir`, `analyze`, `verify`, `verify-project`, `doctor`, and `orchestrate`, the CLI now writes capability artifacts after resolving the selected source frontend.

When auto-detection is used, capability output is written next to `source-detection-report.*`.

Example:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- \
  --mode migrate \
  --input ./JavaTests \
  --target ts \
  --out generated-java-ts \
  --format both
```

Produces:

- `source-detection-report.json/md`
- `source-capabilities-report.json/md`
- generated `.spec.ts` files

## Production meaning

The C# Selenium frontend is the only source currently marked `stable`.

Java Selenium is `experimental-mvp`: useful for common JUnit/TestNG Selenium patterns, but without Java semantic analysis.

Python Selenium is `experimental-spike`: useful for simple pytest/unittest diagnostics and smoke migration, but not production-ready.
