# Orchestrator Dry-Run — Pilot Summary

## Overview

`--mode orchestrate` was implemented as the primary entry point for running the full migration pipeline (analyze → migrate → verify → propose) in a single deterministic dry-run. It replaces the manual 4-step workflow with an automated, non-destructive assessment tool.

## Implementation

### Core Components

**`OrchestrationReport` model** (`Migrator.Core/OrchestrationReport.cs`):
- `OrchestrationReport` — unified report with stages, metrics, top proposals, recommended next actions, warnings
- `OrchestrationStage` — per-stage status, exit code, message, output directory
- `OrchestrationMetrics` — aggregated metrics across stages (files, tests, generated, syntax errors, TODOs, proposals)

**Stage status constants** (`Migrator.Core/Orchestrator.cs`):
- `NotStarted`, `InProgress`, `Passed`, `PassedWithWarnings`, `Skipped`, `Failed`
- `PathSanitizer` — sanitizes paths for public reports (relative when possible, falls back to file name)

**CLI integration** (`Migrator.Cli/Program.cs`):
- `RunOrchestrate` — main orchestrator function, runs 4 stages in sequence
- Reuses existing stage implementations (no code duplication)
- Composes unified JSON + markdown reports
- Exit codes: 0 (pass), 1 (verify fail), 2 (invalid input), 3 (stage failure), 4 (syntax errors)

### Pipeline Flow

```
analyze/          ← parse source, catalog targets, generate draft config
    ↓
generated/        ← generate Playwright .NET code, report unmapped/unsupported
    ↓
verify/           ← quality gates, syntax check, TODO scan
    ↓
propose/          ← ranked config improvement proposals
    ↓
orchestration-report.json / .md  ← unified report
```

### Key Design Decisions

1. **Reuses existing pipeline components** — orchestrator reuses the same parser, renderer, and adapter logic used by individual modes. Stage orchestration glue lives in `Program.cs`.
2. **Propose runs on verify failure** — proposals are most actionable when quality gates fail, so propose stage always runs if analyze + migrate succeeded.
3. **In-memory VerifyReport** — verify stage passes `VerifyReport` object directly to propose stage, avoiding JSON round-trip issues (custom JSON format doesn't match the positional record).
4. **GeneratedFiles from generated/report.json** — metrics read generated file count from the migrate stage's report, not the analyze stage's report (where it's 0 before migration).
5. **Read-only** — no config mutation, no auto-apply of proposals.

## Bugs Fixed During Implementation

### VerifyReport JSON Deserialization Failure

**Problem**: `ProposalGenerator.GenerateScopeProposals` threw `ArgumentNullException` on `input.VerifyReport.Files` — the property was null after deserializing `verify-report.json`.

**Root cause**: `VerifyReportWriter.ToJson` writes a custom JSON structure with `summary.*` nested fields and camelCase keys. Deserializing back into the `VerifyReport` positional record (PascalCase, flat properties) left `Files` as null.

**Fix**: Pass in-memory `verifyReport` from verify stage directly to propose stage (`Migrator.Cli/Program.cs:1129`).

### GeneratedFiles Always 0 in Report

**Problem**: `OrchestrationMetrics.GeneratedFiles` showed 0 even when migration produced files.

**Root cause**: Metrics read from `analyze/report.json` where `GeneratedFiles` is 0 (set before migration runs).

**Fix**: Read `GeneratedFiles` from `generated/report.json` where the count is accurate post-migration.

## Test Results

- **181 total tests**, 180 pass, 1 pre-existing failure (`Adapter_TestIdAttribute_SpecialCharsEscaped`)
- **17 orchestrator tests**: model serialization (6), path sanitization (2), CLI integration (9)
- All orchestrator tests green

### Test Coverage

| Category | Tests | Coverage |
|---|---|---|
| Model serialization | 6 | Report JSON round-trip, stage statuses, metrics |
| Path sanitization | 2 | Relative paths, non-relative fallback |
| CLI: full pipeline | 2 | Runs all stages, writes reports |
| CLI: verify fail continue | 1 | Propose runs after verify failure |
| CLI: no config mutation | 1 | adapter-config.json unchanged |
| CLI: no auto-apply | 1 | Proposals in `propose/`, not merged |
| CLI: invalid input | 1 | Returns non-zero exit code |
| CLI: report content | 1 | Valid JSON, correct stage statuses |
| CLI: compatible formats | 1 | Compatible with individual modes |

## Dry-Run Results (TestFiles)

Running orchestrator on `Migrator.Tests/TestFiles/`:

```
Status: passed
  analyze:  5 files, 21 tests
  migrate:  5 files generated
  verify:   passed (79 TODOs, 0 syntax errors)
  propose:  16 proposals generated

Top Proposals:
  1. [High]   Set MaxTodoComments quality gate (score: 203)
  2. [High]   Add TableMapping for `page.Table` (score: 67)
  3. [Medium] Add UiTarget mapping for `page.FuterUser` (score: 29)
```

## Files Changed

| File | Change |
|---|---|
| `Migrator.Core/OrchestrationReport.cs` | New — report, stage, metrics records |
| `Migrator.Core/Orchestrator.cs` | New — stage status constants, PathSanitizer |
| `Migrator.Cli/Program.cs` | Added `RunOrchestrate`, `DetermineOverallStatus`, `DetermineExitCode`, `GenerateRecommendedNextActions`, `ToOrchestrationReportMarkdown` |
| `Migrator.Cli/Program.cs` | Updated `ParseArgs`, `PrintHelp` for `orchestrate` mode |
| `Migrator.Tests/OrchestratorTests.cs` | New — 17 orchestrator tests |
| `docs/profile/orchestrator-dry-run.md` | New — profile documentation |

## Next Steps

- [ ] Run orchestrator on real batch (`arbilling-e2e-tests`) and capture pilot results
- [ ] Add orchestrator to CI pipeline as non-blocking validation
- [ ] Consider incremental mode — skip stages whose input hasn't changed
