# Config layering

Milestone 3 adds layered adapter configs.

Use multiple `--config` arguments from left to right:

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode migrate `
  --input "<Selenium tests>" `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/discounts.adapter.json" `
  --out "discounts-migrate" `
  --format both
```

The first config is the base profile. Later configs override or extend it.

## Merge rules

- `UiTargets`: merged by `SourceExpression`; later config wins.
- `Methods`: merged by `SourceMethod`; later config wins.
- `ParameterizedMethods`: merged by `SourceMethodPattern`; later config wins.
- `PageObjects`: merged by `SourceType`; later config wins.
- `Tables`: merged by `SourceExpression`; later config wins.
- `Pagination`: merged by `SourceExpression`; later config wins.
- `SourceOnlyIdentifiers`: union/distinct.
- `TargetKnownTypes`: union/distinct.
- `TargetKnownIdentifiers`: union/distinct.
- `LocatorSettings`: later non-empty scalar values win; known attributes are unioned.
- `TestHost`: later non-empty scalar values win; `Usings`, `ClassAttributes`, `SetUpStatements` are unioned.
- `Verification`: later non-empty scalar values win; project/package/assembly references are merged.
- `QualityGates`: later non-null values win.
- `Scopes`: merged by `Name`; scope patterns and symbol lists are unioned, scoped mappings merge by their usual keys.

## Agent rule

Reusable project-family rules go to the base profile. Project-only selectors, helpers and references go to the project profile.

Do not duplicate mappings in the project profile if the base profile already maps them correctly.

## Inspecting merged config

`config-validate` accepts the same layers:

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-validate --config profiles/infrastructure-base.adapter.json --config profiles/projects/discounts.adapter.json --out config-validate
```

When more than one layer is provided, it also writes:

```text
adapter-config.merged.json
```

Use this file for review/debug only. Do not edit it as the source of truth.
