# Runtime failure classifier

`runtime-classify` is the first post-smoke diagnostic mode. It does not run tests, does not modify trace files, and does not change source files. It reads Playwright, NUnit, xUnit, JUnit/pytest-style logs when available, plus trace/media artifacts, and turns failures into a runtime feedback loop: root-cause groups, suggested fixes, a smoke rerun plan, and a runtime readiness score.

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
  runtime-feedback-loop.md      # same report, production-facing name
  runtime-feedback-loop.json
  runtime-failure-report.md      # backward-compatible alias
  runtime-failure-report.json    # backward-compatible alias
  runtime-next-tickets.md
  agent-runtime-failure-next-task.md
```

## Feedback loop sections

The report now includes:

- **Runtime readiness score**: `0..100` with levels `ready`, `nearly-ready`, `needs-targeted-fixes`, or `blocked`.
- **Runtime root causes**: grouped runtime causes such as `selector-evidence-gap`, `wait-or-product-state-gap`, `environment-auth-gap`, and `assertion-semantics-gap`.
- **Suggested config/profile/runtime fixes**: small recommendation cards with scope, safety classification, suggested config area, and evidence. These are recommendations only; the command never edits config/profile files.
- **Smoke rerun plan**: the smallest next rerun scope, trace mode, command template, steps, and success criteria.
- **Runtime next tickets**: ticket-shaped output for follow-up work.

The intended loop is:

1. Run one smoke candidate.
2. Run `runtime-classify` on logs/traces.
3. Apply at most one evidence-backed fix.
4. Rerun the same smoke candidate.
5. Run `runtime-classify` again and compare root-cause counts/readiness score.

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

## Safety rules

1. Suggested fixes are not patches and are never applied automatically.
2. Environment/auth/network failures should be fixed before changing `adapter-config`.
3. Locator failures should be checked against trace/screenshot and POM source truth before changing mappings.
4. Assertion mismatches require comparing old Selenium assertion semantics with generated Playwright semantics.
5. Do not blindly increase timeouts. First verify navigation, locator, setup, and product state correctness.
6. Frame/shadow/modal failures usually need target-context modeling, not broad selector changes.
7. If a repeating failure is unclassified, include raw logs, generated file, source migration context, and trace/screenshot evidence in the escalation report.

## How agents should use it

Use `runtime-classify` after running one or a small number of smoke candidates from `smoke-plan`.

Agents should treat the readiness score and suggested fixes as a stop-policy input: if the top root cause is environment/auth, do not edit config; if the top root cause is selector evidence, prove selector lineage first; if assertion semantics changed, compare Selenium source truth before changing expected values.
