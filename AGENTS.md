# Repository agent instructions

This repository no longer uses the legacy root `.agent-loops/` prompt pack as a launch procedure.

## Canonical workflows

- For guarded OpenCode Desktop Selenium → Playwright migration runs, use `docs/guarded-opencode-desktop-runbook.ru.md`.
- For installed product-repo runs, the executable contract is copied into `migration/AGENT_CONTRACT.md`, `migration/state/final-gate.md`, and `migration/scripts/check-*.ps1`.
- For repository development, use normal code-review discipline: small patches, focused tests, and no unrelated refactors.
- For agent-skill-driven migration runs, read `migration/agent-skills/skill-map.md` and only the relevant `SKILL.md` files. Record common role profiles with `migration/scripts/record-agent-skill-profile.ps1` / `.sh`; use `write-agent-skill-usage` for one-off decisions. The skill layer is a behavior aid, not a permission grant.

## Hard rules for this repository

1. Do not edit production/user project files in a migration-artifact run. Generated or proposed target/POM code belongs under `migration/**` in the product repo.
2. Do not treat `0 TODO` as success unless scope, quality, and verification evidence pass the final gate.
3. Do not reduce TODO by adding suppressions, weakening assertions, deleting actions, adding dummy known identifiers, or hiding empty tests.
4. Guard scripts and checksum manifests are security-sensitive. Do not modify them unless the task is explicitly about guardrail implementation and tests are updated.
5. If a change affects prompts, OpenCode config, scope guard, or final gate behavior, update regression tests in `Migrator.Tests/AgentLoopHardeningTests.cs`.
6. Keep lifecycle scripts cross-platform: every new or changed repository `scripts/*.ps1` and migration-kit `templates/migration-kit/**/*.ps1` script needs a sibling `.sh` companion, even if the Unix version is a thin `pwsh` wrapper. Thin wrappers must fail with a clear PowerShell 7 (`pwsh`) install hint on macOS/Linux/WSL and may fall back to `powershell.exe` only for Windows-like Bash shells.

## Verification

Typical repository checks:

```powershell
dotnet build --no-restore
dotnet test Migrator.Tests\Migrator.Tests.csproj --no-restore
```

For docs-only changes, at least run markdown/link sanity checks when available and `git diff --check`.

## CLI installation diagnostics

When diagnosing whether `selenium-pw-migrator` is installed, do not start with `dotnet tool list` only. The CLI may come from standalone install, npm wrapper, dotnet global tool, or dotnet local tool. First inspect what the shell actually resolves:

Windows PowerShell:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
selenium-pw-migrator --version
```

Bash/Linux/macOS/WSL:

```bash
command -v selenium-pw-migrator
which -a selenium-pw-migrator || true
selenium-pw-migrator --version
```

Only after PATH resolution, inspect package managers: `dotnet tool list --global`, `dotnet tool list --local`, `npm list -g selenium-pw-migrator --depth=0`, and npm registry/base-url config. Prefer `scripts/diagnose-install.ps1` or `scripts/diagnose-install.sh` when available.


## OpenCode permission noise rule

Do not ask for permission for routine allowed inspection. Start with actual shell resolution and repository state, not package-manager-specific checks only:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
git status --short --untracked-files=all
git diff --stat
git diff
```

Known migration subagents (`executor`, `watchdog`, `reviewer`, `migration-researcher`, `migration-research-lead`, `migration-task-slicer`, `migration-change-reviewer`, `harness-sentinel`) are allowed by the OpenCode profile. If OpenCode asks for a routine read-only command, prefer using the documented low-noise permission profile rather than changing the migration plan.

Reusable migration skills installed by the kit include `plow-ahead`, `read-the-damn-docs`, `agent-watchdog`, `efficient-frontier`, `quick-recap`, and `plan-arbiter`. They should reduce prompt bloat by being loaded only when the current task needs them. Common role bundles are recorded through `record-agent-skill-profile`; one-off decisions still use `write-agent-skill-usage`.


## OpenCode permission profile note

Default user runs should work with the `LowNoise` profile. Do not rely on only `dotnet tool list`; diagnose the effective CLI with `Get-Command selenium-pw-migrator -All`, `where.exe selenium-pw-migrator`, and `selenium-pw-migrator --version` first.

Maintainer dogfood runs may use `TrustedProject` to suppress routine approval prompts inside the repository. Even in that mode, keep `external_directory: deny` and run the final scope/harness gates before accepting results.



## Permission and append-only state safety

OpenCode permission denials are authoritative. If an edit/write is denied, do not retry the same write through `bash`, PowerShell, Python, `sed`, `tee`, shell redirection, or another write primitive; report `BLOCKED_BY_OPENCODE_PERMISSION_DENIED`. JSONL ledgers are append-only by default: use `migration/scripts/write-harness-event.*` for events/traces, `migration/scripts/record-agent-skill-profile.*` or `migration/scripts/write-agent-skill-usage.*` for applied skills, `selenium-pw-migrator memory add` or `migration/scripts/write-memory-entry.*` for memory, and `migration/scripts/repair-memory-jsonl.*` only for explicit invalid-JSONL repair with a backup.

## Harness continuation strict protocol

Post-final research is not a terminal human handoff: `MANUAL_REVIEW` / `Developer action` items must be reviewed by `migration-research-lead`, sliced by `migration-task-slicer`, and delegated as bounded executor tickets when source truth and allowed scope make that safe.

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. In continuous mode, this one-action rule is a per-cycle safety boundary: after checks/gates, re-read state and immediately run the next authorized bounded cycle until a real terminal condition. After `FINAL`, stop for review and report evidence; start another run only on explicit `continue` or bounded auto-continuation. Stop for a concrete guard/scope/policy blocker with no authorized remediation; `BLOCKED_BY_WAVE_QUALITY_BUDGET` only blocks wave advancement while an actionable in-scope remediation ticket and budget remain, missing input, loop/plateau, or max autonomous budget. If the same Goal/Progress/Next Steps would be emitted twice without new evidence, run `migration/scripts/check-loop-guard.ps1` / `.sh` and stop on `LOOP_GUARD_BLOCKED`.



## `/supervised-task waves` one-command start

When the user starts a fresh migration with `/supervised-task waves`, do not run full-source `selenium-pw-migrator --mode migrate` first. Resolve the repository root first and keep all `migration/**` artifacts under the repository-root `migration/` directory, never under `Web/**/migration/**` or another source/target subdirectory. Before auto-detecting anything, read `migration/state/source-scope.json`, `migration/.migration-kit/version.json`, and `migration/state/memory/project-profile.json`; a configured bootstrap source from `kit bootstrap-opencode --source ...` is authoritative. Do not scan sibling `*FunctionalTests*` projects or widen the migration when a configured source exists. Auto-detect the Selenium source path only when no configured source is present or it is still the `<SOURCE_SELENIUM_PROJECT_PATH>` placeholder. Then run the setup chain yourself: `kit bootstrap-opencode` when the workspace/command pack is missing, `kit doctor`, `migration plan --input <configured-source> --strategy wavefront --wave-profile auto`, `migration run-wave` for the first pending wave, and the wave-local migrate script. Treat `migration/runs/<wave-id>/input-scope.json` as the active bounded scope.

## `/supervised-task` auto-next UX

`/supervised-task` is the normal tester-facing entrypoint and must work with no arguments. Do not ask the user what to do next just because the previous run is `FINAL`. Read `migration/state/continuation-decision.json`, `migration/state/final-gate-result.json`, `migration/current-ticket.md`, and `migration/state/handoff.md`; if continuation is required, execute the named bounded action. If the previous state is a fresh FINAL checkpoint produced in the same supervised-task run, stop for review by default: report evidence, remaining risks, and one recommended `/supervised-task continue` command. If `$ARGUMENTS` contains a standalone `continuous` token or the exact pair `--continuation auto`, normalize that modifier separately from the base request and keep the same invocation running through fresh successful checkpoints, `SAFE_CHECKPOINT`, `CONTINUE_REQUIRED`, persisted `FINAL_STOPPED_FOR_REVIEW`, and eligible next waves. This works for ordinary `/supervised-task`, `continue`, `waves`, `waves fresh`, and bounded requests. It never bypasses DONE, limitations, blocker/human-decision, critical-risk, scope, no-progress, permission, malformed-state, or budget stops; `sentinel`/`inspect`/`qa` remain one-shot. If `/supervised-task` starts and the persisted state is already `FINAL_STOPPED_FOR_REVIEW`, resume the closed post-final loop automatically even with zero arguments. When the user explicitly says `continue` after `FINAL_STOPPED_FOR_REVIEW` without a concrete implementation task, use the same closed post-final loop via `migration-researcher`, `migration-research-lead`, and `migration-task-slicer` instead of asking for a long supervisor prompt or starting implementation immediately. Start implementation only after approved research, task slicing into `migration/current-ticket.md`, a concrete implementation request, or bounded auto-continuation. If `migration/state/start-dispatch.json` or `migration/next-commands.md` exists after `selenium-pw-migrator start`, treat it as the active ticket: run install diagnostics, bootstrap the selected agent handoff if needed, then run pilot. Do not ask a broad menu of repository tasks unless start state is missing or contradictory.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. In default mode reports should say why work stopped and recommend `/supervised-task continue`. In continuous mode the checkpoint is still persisted but is consumed immediately inside the same invocation until a real terminal condition is reached. Before any continuous-mode handoff, veto the report while `mustContinueBeforeUserMessage` is true, a current ticket is non-terminal, `POST_FINAL_TASKS_READY`/`CONTINUE_REQUIRED` remains, or wave quality budget has an actionable remediation next action with budget.



## Harness sentinel / process testing

`harness-sentinel` is the process tester and forensic reviewer. It reads `opencode-session-export.md`, `session-observations.jsonl`, trace/events, state files, prompts, and OpenCode config to find process smells such as permission-bypass attempts, append-only ledger overwrites, contradictory continuation state, premature DONE, stale root OpenCode config, or full-source migration in wave mode.

Before final handoff, create/update `migration/runs/<run-id>/opencode-session-export.md` with `migration/scripts/export-opencode-session.*` when possible, then run `harness-sentinel` and complete `migration/runs/<run-id>/sentinel/sentinel-inspection.json`. Open high/critical agent-executable sentinel findings must be routed into bounded hardening tasks; do not hand them to the user as vague advice.


Sentinel inspections must be finalized with `migration/scripts/complete-sentinel-inspection.ps1` or `.sh`; final gate treats a missing active-run `sentinel-inspection.json` as a process defect.


## Current-ticket lifecycle

When `migration/current-ticket.md` exists, route it through reviewer/executor before selecting a new wave. Use `migration/scripts/update-current-ticket-status.ps1` / `.sh` to track `READY`, `IN_PROGRESS`, `REVIEW_READY`, `DONE`, or `BLOCKED` in `migration/state/current-ticket-status.json` and `migration/state/current-ticket-ledger.jsonl`.

## Bounded wavefront rule

Wavefront migration starts with a one-test smoke wave. The auto profile must write `wave-tuning.md/json` without agents and affinity-pack later tests using same-file marginal complexity. Soft limits may produce `SOFT_LIMIT_EXCEEDED` or `HEAVY_SINGLE_TEST`; only `BLOCKED` prevents automatic execution. Stop at `FINAL_WITH_LIMITATIONS` when `REMEDIATION_BUDGET_EXHAUSTED`; do not continue post-final tickets automatically. Use `/supervised-task waves fresh` to archive the pilot while preserving project memory.

- After an agent/session interruption, run `migration plan-agent-recovery` before another dispatch. Honor `WAIT_FOR_ROLE`, use `recover-agent-runtime` only for `SAFE_REPAIR_AVAILABLE`, and never rewrite malformed `agent-role-events.jsonl`. Long roles renew their bounded lease with `heartbeat-agent-role`; freshness comes from the latest heartbeat, and concurrent lease/journal mutations must remain serialized.
