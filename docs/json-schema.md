# Adapter-config JSON Schema

Milestone 9 adds an editor-friendly JSON Schema for `adapter-config.json` and profile layers.

Schema file:

```text
schemas/adapter-config.schema.json
```

Use it at the top of config/profile files:

```json
{
  "$schema": "./schemas/adapter-config.schema.json",
  "SourceProjectName": "My Selenium project"
}
```

For project-local profile files, use a relative path from that profile to the schema, for example:

```json
{
  "$schema": "../../schemas/adapter-config.schema.json"
}
```

## Why this helps

Editors such as VS Code and Rider can suggest known config sections:

- `UiTargets`
- `Methods`
- `ParameterizedMethods`
- `TargetKnownTypes`
- `SourceOnlyIdentifiers`
- `Verification`
- `QualityGates`
- `Tables`
- `Pagination`
- `Scopes`

The schema also documents recommended agent metadata fields such as:

- `SourceTruth`
- `Confidence`
- `RequiresReview`

These metadata fields are intentionally allowed as extra properties. The migrator currently ignores unknown metadata at runtime, but it is useful for human/agent review and future tooling.

## Policy

The schema is an editor/DX aid, not a replacement for runtime safety checks.

Always still run:

```powershell
selenium-pw-migrator --mode config-validate --config adapter-config.json --out config-validate
```

For layered configs, validate the same layer order that migration uses:

```powershell
selenium-pw-migrator --mode config-validate `
  --config profiles/infrastructure-base.adapter.json `
  --config profiles/projects/discounts.adapter.json `
  --out config-validate-discounts
```

## Milestone 12: runtime failure classifier and schema workflow

New command modes:

```powershell
selenium-pw-migrator --mode runtime-classify --input "migration/runtime-logs" --out runtime-failure-classification --format both
selenium-pw-migrator --mode config-schema --out schema --format both
```

`runtime-classify` reads runtime logs after a smoke run and groups failures into locator, timeout, assertion, navigation, auth/environment, setup, and browser-context categories. Use it before changing mappings after a failed Playwright run.

`config-schema` writes `adapter-config.schema.json` into the migration workspace for editor/agent usage. JSON Schema complements but does not replace `config-validate`.

See `docs/runtime-failure-classifier.md` and `docs/config-schema-workflow.md`.

