# SeleniumPlaywrightAstMigrator

Internal dotnet tool for agent-assisted migration of Selenium C# tests to Playwright .NET.

Typical local-tool usage:

```powershell
dotnet tool restore
dotnet tool run selenium-pw-migrator -- --mode migrate --input "./OldTests" --config "./profiles/base.adapter.json" --out "migration-run" --format both
```

Cross-platform migration kit workspace:

```bash
selenium-pw-migrator kit init --workspace migration --source ./OldTests
selenium-pw-migrator kit update --workspace migration --backup
selenium-pw-migrator kit doctor --workspace migration
selenium-pw-migrator kit next-ticket --workspace migration
```

Useful modes:

- `kit init/update/doctor/next-ticket` — install/update/check the agent migration workspace on Windows/macOS/Linux.
- `migrate` — generate Playwright .NET tests.
- `verify-project` — compile generated tests in a temporary project-aware harness.
- `index-pom` — extract POM/source-truth facts.
- `explain-todo` — explain remaining TODO/root causes.
- `smoke-plan` — rank tests by runtime readiness.
- `config-validate`, `config-diff`, `guard` — keep agent changes safe.

See repository docs:

- `docs/packaging-and-distribution.md`
- `docs/tool-installation.md`
- `docs/migration-kit-mvp4.md`
- `docs/agent-config-guidelines.md`

## Helper body inventory

Use `--mode helper-inventory` to scan Selenium helper/POM method bodies and generate reviewable MethodSemantics candidates. This mode does not modify config or source files.

```bash
selenium-pw-migrator --mode helper-inventory --input ./selenium_tests --out migration/helper-inventory --format both
```
