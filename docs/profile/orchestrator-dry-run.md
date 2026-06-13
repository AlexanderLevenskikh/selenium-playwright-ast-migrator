# Orchestrator Dry-Run (`--mode orchestrate`)

## Purpose

`--mode orchestrate` runs the full migration pipeline — **analyze → migrate → verify → propose** — in a single deterministic dry-run. It produces a unified report without mutating config or auto-applying proposals.

This mode is the primary entry point for:
- **Pilot assessment** — run on a batch of test files to evaluate migration readiness
- **Iterative improvement** — re-run after updating `adapter-config.json` to measure quality deltas
- **CI integration** — non-destructive validation that migration output meets quality gates

## Usage

```bash
dotnet run --project Migrator.Cli -- --mode orchestrate --input ./SeleniumTests/ --config ./adapter-config.json --out ./orchestration-results --format both
```

### Flags

| Flag | Description |
|---|---|
| `--mode orchestrate` | Run full pipeline dry-run |
| `--input <path>` | Input `.cs` file or directory (required) |
| `--config <path>` | Adapter config for target mapping (optional) |
| `--out <dir>` | Output directory (default: `./orchestration`) |
| `--format <json\|md\|both>` | Report format (default: `both`) |

## Pipeline Stages

Orchestrator runs stages in fixed order. Each stage writes artifacts to its subdirectory:

| Stage | Directory | Artifacts | Behavior |
|---|---|---|---|
| **analyze** | `analyze/` | `report.json`, `unmapped-targets.json`, `adapter-config.draft.json` | Parses source, catalogs targets |
| **migrate** | `generated/` | `*.cs`, `report.json`, `unmapped-targets.json`, `unsupported-actions.json` | Generates Playwright code |
| **verify** | `verify/` | `verify-report.json`, `verify-report.txt` | Quality gates, syntax check, TODO scan |
| **propose** | `propose/` | `mapping-proposals.json`, `mapping-proposals.md` | Ranked config improvement proposals |

### Stage Failure Semantics

- **analyze fails** → remaining stages skipped, exit code 3
- **migrate fails** → verify and propose skipped, exit code 3
- **verify fails** → propose still runs (proposals are most useful when quality gates fail), exit code 1
- **propose fails** → non-critical, reported as warning, exit code from verify stage

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | All stages passed, quality gates satisfied |
| 1 | Verify or quality gates failed (propose still ran) |
| 2 | Invalid input (directory not found, no `.cs` files) |
| 3 | Stage failure (analyze or migrate error) |
| 4 | Syntax errors in generated code |

## Output Artifacts

### `orchestration-report.json`

Unified JSON report with:

```json
{
  "Status": "passed",
  "InputPath": "TestFiles",
  "Stages": [
    { "Name": "analyze", "Status": "passed", "ExitCode": 0, "Message": "5 files, 21 tests" },
    { "Name": "migrate", "Status": "passed", "ExitCode": 0, "Message": "5 files generated" },
    { "Name": "verify",  "Status": "passed", "ExitCode": 0, "Message": "passed" },
    { "Name": "propose", "Status": "passed", "ExitCode": 0, "Message": "16 proposals generated" }
  ],
  "Metrics": {
    "FilesProcessed": 5,
    "TestsFound": 21,
    "GeneratedFiles": 5,
    "SyntaxErrors": 0,
    "TodoComments": 79,
    "PageTodoCalls": 0,
    "Proposals": 16
  },
  "TopProposals": [
    "[High] Set MaxTodoComments quality gate (score: 203)",
    "[High] Add TableMapping for `page.Table` (score: 67)"
  ],
  "RecommendedNextActions": [
    "Add source-truth UiTarget mappings for 31 unmapped target(s)...",
    "Review mapping-proposals.md for suggested config improvements",
    "Re-run orchestrator after applying changes to verify improvement"
  ],
  "Warnings": []
}
```

### `orchestration-report.md`

Human-readable markdown report with stages table, metrics, top 5 proposals, and recommended next actions.

### Path Sanitization

Reports use relative paths where possible. Absolute paths are replaced with file names to avoid leaking internal directory structures in public reports.

## Dry-Run Guarantees

- **No config mutation** — `adapter-config.json` is never modified
- **No auto-apply** — proposals written to `propose/` subdirectory, never merged into config
- **No runtime execution** — only deterministic tool stages run
- **Deterministic output** — same input produces identical output across runs

## Recommended Workflow

```
1. Run orchestrator on target batch
2. Review orchestration-report.md for overall status
3. Review propose/mapping-proposals.md for top proposals
4. Apply high-priority proposals to adapter-config.json
5. Re-run orchestrator to verify quality improvement
6. Repeat until quality gates pass
```

## Architecture

Orchestrator reuses existing stage implementations from `Program.cs` rather than duplicating logic. Each stage writes to its subdirectory, and the orchestrator composes results into the unified report.

Key files:
- `Migrator.Core/OrchestrationReport.cs` — `OrchestrationReport`, `OrchestrationStage`, `OrchestrationMetrics` records
- `Migrator.Core/Orchestrator.cs` — stage status constants, `PathSanitizer` utility
- `Migrator.Cli/Program.cs` — `RunOrchestrate` function, stage composition, report generation

## Integration with Individual Modes

Orchestrator is a convenience wrapper around individual modes. The artifacts produced by orchestrator are identical to running each mode separately:

```
orchestrate == analyze + migrate + verify + propose (with unified report)
```

This means you can:
- Use orchestrator for batch assessment
- Use individual modes for targeted iteration (e.g., re-run only verify after fixing generated code)

## Testing

17 orchestrator tests cover:
- Model serialization (JSON round-trip, stage statuses, metrics)
- Path sanitization
- CLI integration (full pipeline run, report generation, verify-fail-continue, no config mutation, no auto-apply)

See `Migrator.Tests/OrchestratorTests.cs`.
