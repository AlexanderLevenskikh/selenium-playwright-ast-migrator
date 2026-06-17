# bootstrap-project mode

`bootstrap-project` creates a profile skeleton for a new Selenium project.

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode bootstrap-project `
  --input "<Selenium tests or project dir>" `
  --out "bootstrap-discounts" `
  --format both
```

Generated files:

```text
migration/bootstrap-discounts/
  profiles/
    infrastructure-base.adapter.json
    projects/<project>.adapter.json
  migration-profile-plan.md
  agent-next-task.md
  bootstrap-project-report.md/json
```

The generated configs are drafts. Review them before use.

Recommended next steps:

1. Run `index-pom`.
2. Move high-confidence shared rules to the base profile.
3. Move project-specific rules to the project profile.
4. Run `config-validate` with all layers.
5. Run `migrate` and `verify-project` with all layers.
