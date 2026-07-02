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
