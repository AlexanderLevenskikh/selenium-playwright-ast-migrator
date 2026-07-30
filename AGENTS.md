# Repository agent instructions

## Canonical migration workflow

This repository uses one standard full-project source scope. Hidden partition planning and partition-local execution are not supported.

1. Run focused repository checks for code changes.
2. For a product migration, resolve the configured Selenium source from `migration/state/source-scope.json`; if it is missing, stop with `SOURCE_SCOPE_MISSING` instead of guessing.
3. Run an optional representative `pilot` once for calibration.
4. Run the complete source through `selenium-pw-migrator run` and run real matching `verify-project` when available.
5. Rank repeated root causes and fix one highest-payoff root cause per remediation cycle.
6. An ordinary, `continue`, or `continuous` `/supervised-task` invocation may execute up to five cycles. After progress, continue automatically; after one no-progress cycle, try a different independent candidate; stop only after two consecutive distinct no-progress cycles or another mandatory stop.
7. Keep generated syntax, migration metrics, project build verification, and runtime verification separate. A known verification-harness defect does not block independent measurable migration improvements.
8. Do not ask whether to continue while safe agent-executable candidates remain and invocation budget is available. Ask only for a concrete human product decision or authorization.

## Hard rules

1. Do not edit source/product files during a migration-artifact run unless explicitly authorized.
2. Generated or proposed target/POM code belongs under `migration/**` until reviewed.
3. Do not treat low TODO count or zero syntax errors as a passing project build.
4. Do not reduce TODO by suppressing assertions, deleting actions, hiding empty tests, or inventing mappings.
5. Never create validation-result JSON manually to bypass a failed CLI command.
6. Keep changed PowerShell scripts paired with a `.sh` companion when distributed cross-platform.
7. Rewrite `migration/state/handoff.md` completely and validate it; never append duplicate fields or sections.

## Verification

```powershell
dotnet build --no-restore
dotnet test Migrator.Tests\Migrator.Tests.csproj --no-restore
```

For migration smoke checks:

```powershell
selenium-pw-migrator pilot --input ./OldTests --out migration/pilot
selenium-pw-migrator run --input ./OldTests --config ./adapter-config.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input ./OldTests --config ./adapter-config.json --out migration/runs/run-001/verify-project --format both
```

## CLI installation diagnostics

Check the executable actually resolved by the shell before inspecting package managers.

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
