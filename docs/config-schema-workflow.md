# Config schema workflow

`adapter-config` and profile layers support JSON Schema hints for editors and agents.

## Generate/copy schema

```powershell
selenium-pw-migrator --mode config-schema --out schema --format both
```

This writes:

```text
migration/schema/
  adapter-config.schema.json
  adapter-config.schema.usage.md
  config-schema-report.json
```

## Use in config files

At the top of `adapter-config.json` or a profile layer:

```json
{
  "$schema": "./schemas/adapter-config.schema.json"
}
```

For nested profile files, use a relative path from the profile file to the schema.

## Important

JSON Schema is for editor autocomplete and obvious shape/type mistakes. It does not replace safety checks.

After agent changes always run:

```powershell
selenium-pw-migrator --mode config-validate --config adapter-config.json --out config-validate
```

For layered profiles:

```powershell
selenium-pw-migrator --mode config-validate --config profiles/infrastructure-base.adapter.json --config profiles/projects/my-project.adapter.json --out config-validate
```
