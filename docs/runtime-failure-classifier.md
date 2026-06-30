# Runtime failure classifier

`runtime-classify` is the first post-smoke diagnostic mode. It does not run tests, does not modify trace files, and does not change source files. It reads Playwright, NUnit, xUnit, JUnit/pytest-style logs when available, plus trace/media artifacts, and groups failures into actionable categories.

## Command

```powershell
selenium-pw-migrator --mode runtime-classify --input "migration/runtime-logs" --out runtime-failure-classification --format both
```

`--input` can be:

- a single runtime log/report file;
- a Playwright trace `.zip`;
- a directory containing logs, Playwright trace zips, screenshots, videos, console/network artifacts, generated files, and migration reports.

Text logs are read from `.log`, `.txt`, `.md`, `.json`, `.trx`, and `.xml` files. Binary trace/media files are indexed as evidence and are never modified.

## Outputs

```text
migration/runtime-failure-classification/
  runtime-classification.md
  runtime-classification.json
  runtime-failure-report.md      # backward-compatible alias
  runtime-failure-report.json    # backward-compatible alias
  runtime-next-tickets.md
  agent-runtime-failure-next-task.md
```

## Categories

The classifier recognizes these public runtime categories:

- `locator-not-found`
- `strict-mode-violation`
- `timeout-wait-state`
- `assertion-mismatch`
- `navigation-route-missing`
- `auth/session-not-ready`
- `test-data-missing`
- `modal/dialog-state`
- `frame/shadow-dom`
- `environment/flaky-infra`
- `unclassified-runtime-failure`

Each group includes severity, likely cause, suggested action, and a **likely owner** bucket:

- `config/profile`
- `source truth`
- `target infra`
- `test data`
- `product semantics`
- `manual triage`

## Trace-aware evidence

When trace/media artifacts are present, the report includes a `TraceArtifacts` section with:

- Playwright trace zip files;
- screenshots;
- videos;
- console/network logs such as `.har`, `.trace`, `.network`, or `.jsonl` files.

Trace zips are only inspected enough to identify that they look like Playwright trace evidence. The classifier does not unpack, rewrite, or delete them. If trace parsing fails, classification continues with log-only evidence.

## Generated/source context links

When generated files and migration reports are present in the artifact directory, the classifier attempts to link each runtime observation to:

- the generated Playwright `.cs` file and line from stack traces;
- a generated test file inferred from the test name;
- a source file or source line hint from migration reports/generated comments when available.

These links are best-effort evidence hints. They should guide investigation, not replace source truth.

## How agents should use it

Use `runtime-classify` after running one or a small number of smoke candidates from `smoke-plan`.

Rules:

1. Environment/auth/network failures should be fixed before changing `adapter-config`.
2. Locator failures should be checked against trace/screenshot and POM source truth before changing mappings.
3. Assertion mismatches require comparing old Selenium assertion semantics with generated Playwright semantics.
4. Do not blindly increase timeouts. First verify navigation, locator, setup, and product state correctness.
5. Frame/shadow/modal failures usually need target-context modeling, not broad selector changes.
6. If a repeating failure is unclassified, include raw logs, generated file, source migration context, and trace/screenshot evidence in the escalation report.
