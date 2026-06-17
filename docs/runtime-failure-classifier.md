# Runtime failure classifier

`runtime-classify` is the first post-smoke diagnostic mode. It does not run tests and does not change source files. It reads Playwright/NUnit runtime logs and groups failures into actionable categories.

## Command

```powershell
selenium-pw-migrator --mode runtime-classify --input "migration/runtime-logs" --out runtime-failure-classification --format both
```

`--input` can be either a single log/report file or a directory containing `.log`, `.txt`, `.md`, `.json`, `.trx`, or `.xml` files.

## Outputs

```text
migration/runtime-failure-classification/
  runtime-failure-report.md
  runtime-failure-report.json
  agent-runtime-failure-next-task.md
```

## Categories

The classifier recognizes common runtime failure families:

- `locator-not-found`
- `locator-strict-mode`
- `timeout-wait`
- `navigation-timeout`
- `navigation-failed`
- `assertion-mismatch`
- `auth-or-permissions`
- `server-environment`
- `network-environment`
- `test-data-or-setup`
- `browser-context-closed`
- `unclassified-runtime-failure`

## How agents should use it

Use `runtime-classify` after running one or a small number of smoke candidates from `smoke-plan`.

Rules:

1. Environment/auth/network failures should be fixed before changing `adapter-config`.
2. Locator failures should be checked against trace/screenshot and POM source truth before changing mappings.
3. Assertion mismatches require comparing old Selenium assertion semantics with generated Playwright semantics.
4. Do not blindly increase timeouts. First verify navigation, locator, and setup correctness.
5. If a repeating failure is unclassified, include it in an escalation report or add a new classifier pattern.
