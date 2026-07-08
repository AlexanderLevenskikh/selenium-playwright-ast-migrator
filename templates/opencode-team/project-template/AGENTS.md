# Project rules for agents

These rules are intended for OpenCode agents working in this repository.

## Scope discipline

- Prefer minimal, reviewable changes.
- Do not refactor unrelated code.
- Do not rename public APIs unless explicitly requested.
- Do not change generated files unless the task explicitly says so.
- Do not solve adjacent problems unless the user asks for them.
- If the requested task is ambiguous, make a reasonable narrow assumption and state it.

## Verification

- For C# changes: run the smallest relevant `dotnet test` or `dotnet build`.
- For TypeScript changes: run focused tests/lint/typecheck when available.
- For Playwright changes: prefer focused test runs before broad runs.
- If verification is skipped, explain why.
- Never claim completion if required verification was not run.

## Git safety

- Never commit.
- Never push.
- Never run destructive git commands without explicit user request.
- Never delete files without explicit user request.

## Reporting

Final report must include:
- changed files;
- verification result;
- remaining risks;
- anything intentionally not fixed.

## Code quality

- Prefer existing project patterns.
- Avoid broad rewrites.
- Avoid speculative abstractions.
- Add regression tests when fixing bugs if a suitable test location exists.
- Do not hide TODOs by suppressing diagnostics unless explicitly justified.


## Harness Kit workflow

Use these rules for migration-artifact/autopilot tasks:

- Treat English docs/prompts as canonical; Russian docs are secondary localization.
- Create or resume a Harness Kit run before implementation.
- Read `migration/state/harness-policy.json` before planning.
- Read the active run files before editing: `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
- Pass the active run id to executor/reviewer/watchdog tasks.
- Do not ask routine continuation questions when the next action is allowed by `harness-policy.json` and local permissions.
- Record important decisions, verification, and risks in `Documentation.md`.
- Use `migration/state/harness-events.jsonl` / `trace.jsonl` for meaningful events; do not fake command results.
- Run `migration/scripts/check-scope.ps1` and `migration/scripts/check-harness-policy.ps1` after edits when available.
- Treat `migration/scripts/check-final-gate.ps1` as the only final acceptance gate for guarded migration runs.

## Migrator-specific notes

Use these only for Selenium → Playwright migrator tasks:

- Keep migrations target-safe.
- Prefer semantic/Roslyn-based fixes over string hacks.
- Preserve compile-smoke expectations.
- New mappings should have regression tests when possible.
- Do not suppress unsupported actions just to reduce counters.
- Generated output should remain deterministic.
- If adapter config changes, explain why.

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


## OpenCode permission profile note

Default user runs should work with the `LowNoise` profile. Do not rely on only `dotnet tool list`; diagnose the effective CLI with `Get-Command selenium-pw-migrator -All`, `where.exe selenium-pw-migrator`, and `selenium-pw-migrator --version` first.

Maintainer dogfood runs may use `TrustedProject` to suppress routine approval prompts inside the repository. Even in that mode, keep `external_directory: deny` and run the final scope/harness gates before accepting results.



## Permission and append-only state safety

OpenCode permission denials are authoritative. If an edit/write is denied, do not retry the same write through `bash`, PowerShell, Python, `sed`, `tee`, shell redirection, or another write primitive; report `BLOCKED_BY_OPENCODE_PERMISSION_DENIED`. JSONL ledgers are append-only by default: use `migration/scripts/write-harness-event.*` for events/traces, `selenium-pw-migrator memory add` or `migration/scripts/write-memory-entry.*` for memory, and `migration/scripts/repair-memory-jsonl.*` only for explicit invalid-JSONL repair with a backup.

## Harness continuation strict protocol

Post-final research is not a terminal human handoff: `MANUAL_REVIEW` / `Developer action` items must be reviewed by `migration-research-lead`, sliced by `migration-task-slicer`, and delegated as bounded executor tickets when source truth and allowed scope make that safe.

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. A fresh `FINAL` checkpoint stops once for review and reports evidence; any later `/supervised-task` where `harness-run.json` is already `FINAL_STOPPED_FOR_REVIEW` resumes the closed post-final loop automatically. Stop for guard/scope/policy blocker, missing input, loop/plateau, or max autonomous budget.



## `/supervised-task waves` one-command start

When the user starts a fresh migration with `/supervised-task waves`, do not run full-source `selenium-pw-migrator --mode migrate` first. Resolve the repository root first and keep all `migration/**` artifacts under the repository-root `migration/` directory, never under `Web/**/migration/**` or another source/target subdirectory. Auto-detect the Selenium source path, target backend/framework, and existing Playwright target when possible; ask only for missing or ambiguous required inputs. Then run the setup chain yourself: `kit bootstrap-opencode` when the workspace/command pack is missing, `kit doctor`, `migration plan --strategy wavefront`, `migration run-wave` for the first pending wave, and the wave-local migrate script. Treat `migration/runs/<wave-id>/input-scope.json` as the active bounded scope.

## `/supervised-task` auto-next UX

`/supervised-task` is the normal tester-facing entrypoint and must work with no arguments. Do not ask the user what to do next just because the previous run is `FINAL`. Read `migration/state/continuation-decision.json`, `migration/state/final-gate-result.json`, `migration/current-ticket.md`, and `migration/state/handoff.md`; if continuation is required, execute the named bounded action. If the previous state is a fresh FINAL checkpoint produced in the same supervised-task run, stop for review by default: report evidence, remaining risks, and one recommended `/supervised-task continue` command. If `/supervised-task` starts and the persisted state is already `FINAL_STOPPED_FOR_REVIEW`, resume the closed post-final loop automatically even with zero arguments. When the user explicitly says `continue` after `FINAL_STOPPED_FOR_REVIEW` without a concrete implementation task, use the same closed post-final loop via `migration-researcher`, `migration-research-lead`, and `migration-task-slicer` instead of asking for a long supervisor prompt or starting implementation immediately. Start implementation only after approved research, task slicing into `migration/current-ticket.md`, a concrete implementation request, or bounded auto-continuation. If `migration/state/start-dispatch.json` or `migration/next-commands.md` exists after `selenium-pw-migrator start`, treat it as the active ticket: run install diagnostics, bootstrap the selected agent handoff if needed, then run pilot. Do not ask a broad menu of repository tasks unless start state is missing or contradictory.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.



## Harness sentinel / process testing

`harness-sentinel` is the process tester and forensic reviewer. It reads `opencode-session-export.md`, `session-observations.jsonl`, trace/events, state files, prompts, and OpenCode config to find process smells such as permission-bypass attempts, append-only ledger overwrites, contradictory continuation state, premature DONE, stale root OpenCode config, or full-source migration in wave mode.

Before final handoff, create/update `migration/runs/<run-id>/opencode-session-export.md` with `migration/scripts/export-opencode-session.*` when possible, then run `harness-sentinel` and complete `migration/runs/<run-id>/sentinel/sentinel-inspection.json`. Open high/critical agent-executable sentinel findings must be routed into bounded hardening tasks; do not hand them to the user as vague advice.


Sentinel inspections must be finalized with `migration/scripts/complete-sentinel-inspection.ps1` or `.sh`; final gate treats a missing active-run `sentinel-inspection.json` as a process defect.
