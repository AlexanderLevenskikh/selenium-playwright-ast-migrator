# Adapter-config versioning

`adapter-config/v1` is the stable public profile format for project-specific migration knowledge.

A config may include both the editor schema hint and the explicit profile version:

```json
{
  "$schema": "./schemas/adapter-config.schema.json",
  "SchemaVersion": "adapter-config/v1",
  "SourceProjectName": "Example",
  "UiTargets": []
}
```

Existing configs without `SchemaVersion` are still treated as `adapter-config/v1` by the current loader. New public examples should include it so humans, editors, and agents know which contract they are editing.

## Versioning policy

- `adapter-config/v1` is the stable compatibility contract for current public migrations.
- New optional fields may be added to v1 when they are backwards-compatible.
- Breaking changes require a new version and a migration path.
- `migration-profile/v2` is an experimental normalized shape for future source/target/project separation; do not assume every CLI mode accepts it directly.

## Schema files

The repository ships:

```text
schemas/adapter-config.schema.json
schemas/migration-profile-v2.schema.json
```

Generate or copy the adapter schema into a workspace with:

```bash
selenium-pw-migrator --mode config-schema --out schema --format both
```

JSON Schema is an editor and agent aid. It does not replace runtime safety checks:

```bash
selenium-pw-migrator --mode config-validate \
  --config adapter-config.json \
  --target playwright-typescript \
  --validation-mode production \
  --out config-validate
```

## Stable vs internal fields

Stable public fields include the mapping and safety surfaces used by public docs:

- `SchemaVersion`;
- `SourceProjectName`;
- `SourceOnlyIdentifiers`;
- `TargetKnownTypes` / `TargetKnownIdentifiers`;
- `UiTargets`;
- `PageObjects`;
- `Methods`;
- `ParameterizedMethods`;
- `Tables`;
- `Pagination`;
- `NavigationUrls`;
- `WaitPolicies`;
- `Scopes`;
- `QualityGates`;
- `Verification`;
- target-specific `Targets.<target>` blocks.

Agent metadata such as `SourceTruth`, `Confidence`, `RequiresReview`, and notes can appear in mapping objects when the schema allows additional properties. Treat those as review breadcrumbs unless a command explicitly documents them as executable behavior.
