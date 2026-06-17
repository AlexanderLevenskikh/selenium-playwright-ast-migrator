# SeleniumPlaywrightAstMigrator

Internal dotnet tool for agent-assisted migration of Selenium C# tests to Playwright .NET.

Typical local-tool usage:

```powershell
dotnet tool restore
dotnet tool run selenium-pw-migrator -- --mode migrate --input "./OldTests" --config "./profiles/base.adapter.json" --out "migration-run" --format both
```

Useful modes:

- `migrate` — generate Playwright .NET tests.
- `verify-project` — compile generated tests in a temporary project-aware harness.
- `index-pom` — extract POM/source-truth facts.
- `explain-todo` — explain remaining TODO/root causes.
- `smoke-plan` — rank tests by runtime readiness.
- `config-validate`, `config-diff`, `guard` — keep agent changes safe.

See repository docs:

- `docs/packaging-and-distribution.md`
- `docs/tool-installation.md`
- `docs/agent-config-guidelines.md`
