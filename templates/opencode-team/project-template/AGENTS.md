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

Known migration subagents (`executor`, `watchdog`, `reviewer`, `migration-researcher`, `migration-research-lead`, `migration-task-slicer`, `migration-change-reviewer`) are allowed by the OpenCode profile. If OpenCode asks for a routine read-only command, prefer using the documented low-noise permission profile rather than changing the migration plan.


## OpenCode permission profile note

Default user runs should work with the `LowNoise` profile. Do not rely on only `dotnet tool list`; diagnose the effective CLI with `Get-Command selenium-pw-migrator -All`, `where.exe selenium-pw-migrator`, and `selenium-pw-migrator --version` first.

Maintainer dogfood runs may use `TrustedProject` to suppress routine approval prompts inside the repository. Even in that mode, keep `external_directory: deny` and run the final scope/harness gates before accepting results.


## Harness continuation strict protocol

Post-final research is not a terminal human handoff: `MANUAL_REVIEW` / `Developer action` items must be reviewed by `migration-research-lead`, sliced by `migration-task-slicer`, and delegated as bounded executor tickets when source truth and allowed scope make that safe.

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. After `FINAL`, stop for review and report evidence; start another run only on explicit `continue` or bounded auto-continuation. Stop for guard/scope/policy blocker, missing input, loop/plateau, or max autonomous budget.


## `/supervised-task` auto-next UX

`/supervised-task` is the normal tester-facing entrypoint and must work with no arguments. Do not ask the user what to do next just because the previous run is `FINAL`. Read `migration/state/continuation-decision.json`, `migration/state/final-gate-result.json`, `migration/current-ticket.md`, and `migration/state/handoff.md`; if continuation is required, execute the named bounded action. If the previous state is FINAL, stop for review by default: report evidence, remaining risks, and one recommended `/supervised-task continue` command. When the user explicitly says `continue` after `FINAL_STOPPED_FOR_REVIEW` without a concrete implementation task, launch the closed post-final loop via `migration-researcher`, `migration-research-lead`, and `migration-task-slicer` instead of asking for a long supervisor prompt or starting implementation immediately. Start implementation only after approved research, task slicing into `migration/current-ticket.md`, a concrete implementation request, or bounded auto-continuation. If `migration/state/start-dispatch.json` or `migration/next-commands.md` exists after `selenium-pw-migrator start`, treat it as the active ticket: run install diagnostics, bootstrap the selected agent handoff if needed, then run pilot. Do not ask a broad menu of repository tasks unless start state is missing or contradictory.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.

