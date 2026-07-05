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

Known migration subagents (`executor`, `watchdog`, `reviewer`) are allowed by the OpenCode profile. If OpenCode asks for a routine read-only command, prefer using the documented low-noise permission profile rather than changing the migration plan.
