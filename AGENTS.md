# Repository agent instructions

This repository no longer uses the legacy root `.agent-loops/` prompt pack as a launch procedure.

## Canonical workflows

- For guarded OpenCode Desktop Selenium → Playwright migration runs, use `docs/guarded-opencode-desktop-runbook.ru.md`.
- For installed product-repo runs, the executable contract is copied into `migration/AGENT_CONTRACT.md`, `migration/state/final-gate.md`, and `migration/scripts/check-*.ps1`.
- For repository development, use normal code-review discipline: small patches, focused tests, and no unrelated refactors.

## Hard rules for this repository

1. Do not edit production/user project files in a migration-artifact run. Generated or proposed target/POM code belongs under `migration/**` in the product repo.
2. Do not treat `0 TODO` as success unless scope, quality, and verification evidence pass the final gate.
3. Do not reduce TODO by adding suppressions, weakening assertions, deleting actions, adding dummy known identifiers, or hiding empty tests.
4. Guard scripts and checksum manifests are security-sensitive. Do not modify them unless the task is explicitly about guardrail implementation and tests are updated.
5. If a change affects prompts, OpenCode config, scope guard, or final gate behavior, update regression tests in `Migrator.Tests/AgentLoopHardeningTests.cs`.

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

Known migration subagents (`executor`, `watchdog`, `reviewer`, `migration-researcher`, `migration-change-reviewer`) are allowed by the OpenCode profile. If OpenCode asks for a routine read-only command, prefer using the documented low-noise permission profile rather than changing the migration plan.


## OpenCode permission profile note

Default user runs should work with the `LowNoise` profile. Do not rely on only `dotnet tool list`; diagnose the effective CLI with `Get-Command selenium-pw-migrator -All`, `where.exe selenium-pw-migrator`, and `selenium-pw-migrator --version` first.

Maintainer dogfood runs may use `TrustedProject` to suppress routine approval prompts inside the repository. Even in that mode, keep `external_directory: deny` and run the final scope/harness gates before accepting results.


## Harness continuation strict protocol

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. After `FINAL`, stop for review and report evidence; start another run only on explicit `continue` or bounded auto-continuation. Stop for guard/scope/policy blocker, missing input, loop/plateau, or max autonomous budget.


## `/supervised-task` auto-next UX

`/supervised-task` is the normal tester-facing entrypoint and must work with no arguments. Do not ask the user what to do next just because the previous run is `FINAL`. Read `migration/state/continuation-decision.json`, `migration/state/final-gate-result.json`, `migration/current-ticket.md`, and `migration/state/handoff.md`; if continuation is required, execute the named bounded action. If the previous state is FINAL, stop for review by default: report evidence, remaining risks, and one recommended `/supervised-task continue` command. When the user explicitly says `continue` after `FINAL_STOPPED_FOR_REVIEW` without a concrete implementation task, launch post-final research via `migration-researcher` instead of asking for a long supervisor prompt or starting implementation immediately. Start implementation only after reviewed research, a concrete implementation request, or bounded auto-continuation. If `migration/state/start-dispatch.json` or `migration/next-commands.md` exists after `selenium-pw-migrator start`, treat it as the active ticket: run install diagnostics, bootstrap the selected agent handoff if needed, then run pilot. Do not ask a broad menu of repository tasks unless start state is missing or contradictory.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.

