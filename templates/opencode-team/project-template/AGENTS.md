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

Known migration subagents (`executor`, `watchdog`, `reviewer`) are allowed by the OpenCode profile. If OpenCode asks for a routine read-only command, prefer using the documented low-noise permission profile rather than changing the migration plan.


## OpenCode permission profile note

Default user runs should work with the `LowNoise` profile. Do not rely on only `dotnet tool list`; diagnose the effective CLI with `Get-Command selenium-pw-migrator -All`, `where.exe selenium-pw-migrator`, and `selenium-pw-migrator --version` first.

Maintainer dogfood runs may use `TrustedProject` to suppress routine approval prompts inside the repository. Even in that mode, keep `external_directory: deny` and run the final scope/harness gates before accepting results.


## Harness continuation strict protocol

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. Stop only for `FINAL`, guard/scope/policy blocker, missing input, loop/plateau, or max autonomous budget.
