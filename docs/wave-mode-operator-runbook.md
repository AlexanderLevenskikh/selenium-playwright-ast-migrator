# Wave mode operator runbook

This is an operating guide for an already bootstrapped migration workspace. It does not replace the canonical guarded launch procedure: start from [`guarded-opencode-desktop-runbook.ru.md`](guarded-opencode-desktop-runbook.ru.md) when setting up OpenCode Desktop or a fresh product repository.

Use this guide when `/supervised-task waves` or `/supervised-task continue` has already produced `migration/**` state and you need to decide what to do next without guessing. For the complete list of command modes and aliases, see [`supervised-task-modes.md`](supervised-task-modes.md).

## Mental model

Wave mode is not a blind batch migrator. It is a closed loop:

```text
wave run
  -> verify / scope / final gate
  -> sentinel inspection when needed
  -> gate-followup slicer
  -> current-ticket executor loop
  -> wave quality budget
  -> mapping research memory
  -> feedback bundle when the migrator itself needs improvement
```

The important rule is simple: **do not start the next wave while there is an unresolved gate, current ticket, high/critical sentinel finding, or blocked wave-quality budget.**

## Normal start

From the product repository root:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Then open the repository in OpenCode and run:

```text
/supervised-task waves
```

Wave scope is file-based. If the wave materializes 3 source files, it may still contain 20 tests and hundreds of actions. Reports must say `source files`, `tests`, `actions`, `TODOs`, and `syntax-fallback ratio` explicitly.

## Fast execution contract

Each materialized wave contains `wave-manifest.json` and `execution-policy.json`. Before work, run:

```bash
selenium-pw-migrator migration validate-wave --out migration/runs/<wave-id>
```

Use `fast` by default, `standard` when reviewer participation is required throughout the task, and `audit` for protected/high-risk work. Existing run directories are immutable; execute `run-migrate.ps1`/`.sh` rather than rematerializing them. The wrapper refreshes `wave-status.json` and writes `validation-plan.json`. Execute the recommended checks, record the real command/exit code with `migration record-validation`, create a checkpoint, then build the review bundle. Use `migration resume-wave` after interruption instead of starting over. After each bounded fix cycle run `migration check-progress`; `NO_PROGRESS_DETECTED` means stop retries and invoke watchdog/change strategy. Exact-input cached PASS may skip repeated validation, but reviewer, sentinel, project gates, and final gate remain mandatory.

See [`migration-incremental-pipeline.md`](migration-incremental-pipeline.md) for the artifact contracts and complete command flow.

## Continue safely

For an existing workspace, run:

```text
/supervised-task continue
```

or plain:

```text
/supervised-task
```

The dispatcher must choose the next bounded action in this order:

1. If `migration/current-ticket.md` exists, finish or block that ticket before selecting a new wave.
2. If final gate is blocked and no current ticket exists, run `migration/scripts/slice-gate-followups.ps1 -Workspace migration`.
3. If high/critical agent-executable sentinel findings are open, assign them to bounded tickets.
4. If wave quality budget is blocked, collect mapping research memory before another wave.
5. Only after gates, tickets, findings, and budgets are clear, select the next wave.

## When final gate is blocked

Look at:

```text
migration/state/final-gate-result.json
migration/state/continuation-decision.json
migration/runs/<run-id>/Documentation.md
migration/runs/<run-id>/artifact-hygiene.md
```

If `continuationStatus` or `allowedNextAction` says `BLOCKED_BY_GATE` and there is no current ticket yet, run:

```powershell
migration/scripts/slice-gate-followups.ps1 -Workspace migration
```

or on macOS/Linux/WSL:

```bash
migration/scripts/slice-gate-followups.sh -Workspace migration
```

This creates:

```text
migration/current-ticket.md
migration/state/backlog/gate-followup-tasks.jsonl
migration/state/backlog/gate-followup-backlog.md
migration/state/current-ticket-status.json
```

Then run `/supervised-task continue`. The agent should route `current-ticket.md` through reviewer and executor instead of starting a new wave.

## Current-ticket lifecycle

`current-ticket.md` is the active bounded repair task. It is tracked by:

```text
migration/state/current-ticket-status.json
migration/state/current-ticket-ledger.jsonl
migration/runs/<run-id>/tickets/<ticket-id>.json
```

Statuses:

```text
READY -> IN_PROGRESS -> REVIEW_READY -> DONE
READY -> IN_PROGRESS -> BLOCKED
```

Useful manual commands:

```powershell
migration/scripts/update-current-ticket-status.ps1 -Workspace migration -Status IN_PROGRESS -Source operator
migration/scripts/update-current-ticket-status.ps1 -Workspace migration -Status BLOCKED -Source operator -Reason "Requires source cleanup outside migration/**"
```

The agent should only set `DONE` after reviewer/final-gate evidence supports it.

## Sentinel findings

Sentinel findings are append-only facts. Their lifecycle is tracked separately:

```text
migration/state/sentinel-finding-ledger.jsonl
migration/state/sentinel-finding-status.json
migration/runs/<run-id>/sentinel/sentinel-finding-lifecycle.jsonl
```

Use statuses like:

```text
OPEN
ASSIGNED
FIX_ATTEMPTED
VERIFIED
CLOSED
BLOCKED
NON_AGENT_EXECUTABLE
ACCEPTED_RISK
```

High/critical agent-executable findings block final success until they are `VERIFIED`, `CLOSED`, `NON_AGENT_EXECUTABLE`, or `ACCEPTED_RISK` with evidence.

## Noisy wave / quality budget

After a wave, run or trust final gate to run:

```powershell
migration/scripts/evaluate-wave-quality-budget.ps1 -Workspace migration
```

It writes `wave-quality-budget/v1` evidence:

```text
migration/state/wave-quality-budget.json
migration/state/wave-quality-budget.md
migration/runs/<run-id>/wave-quality-budget.json
migration/runs/<run-id>/wave-quality-budget.md
```

If status is `BLOCKED_BY_WAVE_QUALITY_BUDGET`, do not start the next wave. Run:

```powershell
migration/scripts/collect-mapping-research-memory.ps1 -Workspace migration
```

This writes `mapping-research-memory/v1` evidence:

```text
migration/state/mapping-research-memory.json
migration/state/mapping-research-memory.md
migration/state/mapping-research-candidates.jsonl
```

Use that memory to create one bounded improvement ticket: config mapping, POM mapping, recognizer improvement, renderer improvement, or verify-project harness fix.

## Verify-project failures

Start with:

```text
migration/runs/<run-id>/project-verify-report.json
migration/runs/<run-id>/project-verify-report.md
migration/runs/<run-id>/project-verify-harness.csproj
```

The report includes `verify-project-harness/v1` evidence: CPM detection, skipped build files, imported build files, package-version mode, harness snapshot path, and SHA256. For `NU1008`, check whether the temporary harness disabled central package management and skipped `Directory.Packages.props`.

## Artifact hygiene

Before final handoff, run:

```powershell
migration/scripts/validate-run-artifacts.ps1 -Workspace migration
```

`artifact-hygiene/v1` should confirm:

```text
Plan.md is not polluted with shell/write payloads
Documentation.md does not claim success when final gate is blocked
migration-board/wave-status artifacts carry run-id and wave-id
session export status is REAL_EXPORT or UNAVAILABLE_WITH_REASON
```

If this fails, fix the report artifacts before claiming completion.

## Feedback bundle for the migrator author

When the issue looks like a migrator gap rather than a project cleanup task, create a safe bundle:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

or on macOS/Linux/WSL:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

The bundle uses schema `feedback-bundle/v1`, excludes project source and generated `.cs` samples by default, and writes a `manifest.json`. Review `manifest.json` before sharing. Include generated samples only deliberately:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration -IncludeGeneratedSamples -MaxGeneratedSamples 3
```

Good feedback bundles usually include:

```text
mapping-research-memory.json/md
mapping-research-candidates.jsonl
wave-quality-budget.json/md
project-verify-report.json/md
project-verify-harness.csproj
migration-board.md/json
explain-todo.md
sentinel report/findings when relevant
```

## Operator decision table

| Situation | Do this | Do not do this |
|---|---|---|
| `migration/current-ticket.md` exists | Finish, verify, or block the ticket | Start a new wave |
| `final-gate-result.json` is FAIL/BLOCKED | Run `slice-gate-followups` | Mark run complete |
| High/critical sentinel finding is OPEN | Assign/resolve finding lifecycle | Ignore and continue |
| `BLOCKED_BY_WAVE_QUALITY_BUDGET` | Collect mapping research memory | Generate more noisy files |
| `verify-project` failed | Inspect report + harness snapshot | Treat generated code as verified |
| Report says “complete” but gate is red | Run artifact hygiene and fix docs | Trust the summary |
| User wants to improve migrator | Create feedback bundle | Ask for full private repo |

## Quick health checklist

Before another wave:

```text
[ ] no active current-ticket, or it is DONE/BLOCKED with reason
[ ] final gate does not report unresolved hard failures
[ ] high/critical sentinel findings are closed or explicitly routed
[ ] wave-quality-budget is PASS or mapping research memory was collected
[ ] verify-project status is understood
[ ] artifact-hygiene/v1 is PASS
[ ] Documentation.md says NOT FINAL when gates are blocked
```

## Wave-plan quality check

Before the first wave, read `migration/plan/wave-tuning.md`. The `auto` profile runs a static experiment without agents and should reduce expensive role cycles by grouping tests that reuse the same file/POM context. Only the smoke wave is intentionally singleton. `PASS`, `SOFT_LIMIT_EXCEEDED`, and `HEAVY_SINGLE_TEST` are executable; `BLOCKED` crosses the broad hard ceiling and requires replan.
