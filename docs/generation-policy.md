# Generation Policy

`--generation-policy conservative|balanced|aggressive` controls how much risk the generator may take when emitting mapped helper code.

The policy is intentionally narrow: it does **not** invent selectors, does **not** bypass selector evidence, and does **not** modify source tests. In plain terms, generation policy never invents selectors. It only changes how explicit helper mappings marked with `RequiresReview` are treated during generation and how the risk is reported.

## Policies

| Policy | Behavior | Use when |
|---|---|---|
| `conservative` | Forces mapped helper statements through review-required output. Expect more TODO/manual-review comments and less active generated code. | First onboarding, unknown source truth, public demos, or risky POM/helper-heavy suites. |
| `balanced` | Current default behavior. Mapping-level `RequiresReview` decides whether mapped helper output is active or commented. | Normal migration runs after config has some evidence. |
| `aggressive` | Allows explicit mapped helper statements to emit active code by clearing mapping review flags. Reports include risk annotations. | Late-stage cleanup after selector-evidence, verify-project, runtime-classify, and PR review are available. |

## CLI

```bash
selenium-pw-migrator --mode migrate \
  --input ./OldTests \
  --config ./adapter-config.json \
  --generation-policy conservative \
  --out migration/generated
```

The same flag works with planning/reporting commands such as `runbook`:

```bash
selenium-pw-migrator runbook \
  --input ./OldTests \
  --target dotnet \
  --target-test-framework xunit \
  --generation-policy balanced \
  --out migration/runbook
```

## Config

You can persist the default in adapter config:

```json
{
  "SchemaVersion": "adapter-config/v1",
  "GenerationPolicy": "balanced"
}
```

The CLI flag overrides the config value for the current run.

## Reporting

Migration reports and runbooks include the effective generation policy, description, and risk annotations. Evidence packs naturally carry that metadata because they include the generated reports.

## Safety rules

- No policy invents selectors.
- No policy edits source tests.
- No policy silently suppresses assertions.
- `aggressive` should be paired with `selector evidence`, `verify-project`, `runtime-classify`, and `pr pack` before broad rollout.
