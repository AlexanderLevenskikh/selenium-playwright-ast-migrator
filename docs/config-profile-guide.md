# Config and profile guide

The Migrator is profile-driven. Good migration output depends on reviewable mappings that describe your project: locators, helper methods, test host shape, table/list conventions, waits, and quality gates.

## Profile files

The common file name is `adapter-config.json`. Larger migrations usually use layered configs:

```bash
selenium-pw-migrator --mode orchestrate \
  --input ./SeleniumTests \
  --config ./profiles/infrastructure-base.adapter.json \
  --config ./profiles/projects/billing.adapter.json \
  --out run-001 \
  --format both
```

Later config layers override or extend earlier layers according to the config layering rules.

## Minimum useful config

```json
{
  "$schema": "./schemas/adapter-config.schema.json",
  "SchemaVersion": "adapter-config/v1",
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.Username",
      "TargetExpression": "username",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [],
  "Methods": []
}
```

Use source-truth evidence only. Do not invent `data-testid` values from PageObject names.

## Main profile areas

| Area | Purpose | Read next |
|---|---|---|
| `UiTargets` | Map Selenium PageObject expressions to Playwright locators. | [Locator matching](profile/locator-matching.md) |
| `Methods` | Map project helper calls with fixed semantics. | [Method mappings](profile/method-mappings.md) |
| `ParameterizedMethods` | Map helper calls whose arguments vary. | [Parameterized method mappings](profile/parameterized-method-mappings.md) |
| `Scopes` | Apply file/folder-specific overrides. | [Profile scoping](profile/profile-scoping.md) |
| `TestHost` | Control namespace, base class, setup, usings, and generated class shape. | [Project profile cookbook](user-guide/project-profile-cookbook.md) |
| `QualityGates` | Fail on TODOs, unsupported actions, syntax errors, or risky config. | [Reports and quality gates](user-guide/reports-and-quality-gates.md) |

## Discover before mapping

Use these modes before writing large manual config changes:

```bash
selenium-pw-migrator --mode discover-target --input ./PlaywrightTests --out target-discovery
selenium-pw-migrator --mode index-pom --input ./SeleniumTests --out pom-index --format both
selenium-pw-migrator --mode helper-inventory --input ./SeleniumTests --out helper-inventory --format both
```

- `discover-target` creates a draft inventory from an existing Playwright .NET project.
- `index-pom` extracts selector evidence from Selenium PageObjects.
- `helper-inventory` inspects helper/POM method bodies and suggests MethodSemantics candidates.

All draft output requires review.

## Validate and review changes

```bash
selenium-pw-migrator --mode config-validate --config ./adapter-config.json --validation-mode production --out config-validate
selenium-pw-migrator --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff
```

Use production validation before publishing or handing a profile to another team.

## Schema support

Generate or copy JSON Schema files for editor completion:

```bash
selenium-pw-migrator --mode config-schema --out schema
```

See [Config schema workflow](config-schema-workflow.md) and [Adapter-config versioning](adapter-config-versioning.md).
