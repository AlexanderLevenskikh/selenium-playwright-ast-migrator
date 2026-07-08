# Migration feedback bundles

For the full operator flow around noisy waves, blocked gates, mapping research memory, and safe bundle handoff, see [Wave mode operator runbook](wave-mode-operator-runbook.md).

A feedback bundle is a redacted, project-local evidence pack that users can share when a wave produces many TODOs, syntax fallback actions, unresolved symbols, or verification blockers. The goal is to improve the migrator without asking users to send a full private repository.


## User quick start

From the product repository root, after a noisy wave or failed verification, run:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

On macOS/Linux/WSL with PowerShell 7 installed:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

The packer writes `feedback-bundle/v1` artifacts under `migration/state/feedback-bundles/` and creates a `.zip` plus a `manifest.json`. Before sending the zip, open `manifest.json` and review the `included` list. Project source files and generated `.cs` samples are excluded by default. To include a small generated sample set after review, rerun with `-IncludeGeneratedSamples -MaxGeneratedSamples 3`.

## What to collect

Prefer machine-readable artifacts that describe migration gaps without source secrets:

- `migration/runs/<run-id>/generated/explain-todo.md`
- `migration/runs/<run-id>/generated/migration-board.md`
- `migration/runs/<run-id>/research/todo-inventory.json`
- `migration/runs/<run-id>/research/unresolved-symbols.json`
- `migration/runs/<run-id>/research/mapping-candidates.jsonl`
- `migration/runs/<run-id>/project-verify-report.json` / `.md`
- `project-verify-harness.csproj` snapshot when `verify-project` is involved; it pairs with `HarnessEvidence` / `verify-project-harness/v1` in the JSON report
- selected generated files under `migration/runs/<wave-id>/generated/**`, only after redaction
- `migration/state/memory/config-deltas/**` when the user wants to propose config changes

Do not request credentials, `.env`, `nuget.config`, package registry tokens, real customer data, screenshots with PII, or full proprietary source trees.

## How these artifacts improve the migrator

Use the bundle to classify each repeated failure into one of four product improvements:

1. **Recognizer improvement** — repeated source pattern should become a semantic action instead of syntax fallback.
2. **Renderer improvement** — action is recognized, but generated Playwright shape is wrong or too noisy.
3. **Adapter/config improvement** — project-specific PageObjects, selectors, helper methods, or assertion wrappers need config rules.
4. **Verify harness improvement** — generated code is plausible, but `verify-project` cannot compile because the temporary harness misses references, CPM behavior, framework settings, or source-only helpers.

Each accepted feedback item should become a small fixture-driven test before implementation. Prefer minimal synthetic samples derived from the bundle over copying private code verbatim.

## Intake checklist

For every external bundle:

1. Confirm the user has redacted secrets and private customer data.
2. Store the raw bundle outside the repo if it contains proprietary material.
3. Extract a minimal synthetic fixture under `Migrator.Tests` or `examples/feedback-fixtures/**`.
4. Add one failing test that captures the behavior.
5. Fix recognizer/renderer/config/verify logic.
6. Keep the public issue or changelog entry high-level; do not expose private identifiers.

## Suggested user-facing request

```text
Please run `migration/scripts/create-feedback-bundle.ps1 -Workspace migration` and attach the generated `feedback-bundle/v1` zip after reviewing `manifest.json`. The most useful files inside are:
- `mapping-research-memory.json` / `.md`
- `mapping-research-candidates.jsonl`
- `wave-quality-budget.json`
- `project-verify-report.md/json`
- `project-verify-harness.csproj`
- `migration-board.md`
- `explain-todo.md`

Please do not add secrets, customer names, tokens, screenshots with PII, or unrelated source files. Generated `.cs` samples are optional and should only be included with `-IncludeGeneratedSamples` after review.
```


## Mapping/research memory artifacts

When users provide wave artifacts, prefer asking for `mapping-research-memory/v1` outputs when available: `state/mapping-research-memory.json`, `state/mapping-research-memory.md`, and `state/mapping-research-candidates.jsonl`. These are safer than full project source because they preserve the actionable signals — unresolved symbols, TODO clusters, unmapped targets, syntax-fallback clusters, and verify blockers — while making it easier to create minimal synthetic fixtures and regression tests for the migrator.


## Verify harness feedback

For `verify-project` issues, the most useful pair is `project-verify-report.json` plus `project-verify-harness.csproj`. The report contains `HarnessEvidence` with schema `verify-project-harness/v1`, including CPM detection, skipped `Directory.Packages.props`, imported build files, snapshot SHA256, and the selected `NU1008` mitigation. This lets maintainers distinguish:

- generated code compile errors;
- missing project/package references;
- restore/feed problems;
- Central Package Management isolation bugs;
- external props/targets that changed the temporary harness.
