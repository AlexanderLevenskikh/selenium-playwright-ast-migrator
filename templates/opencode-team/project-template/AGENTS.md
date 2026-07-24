# Repository agent instructions

## Canonical migration workflow

This repository uses one standard full-project migration flow. Partition planning and partition-local execution are not supported.

1. Run focused repository checks for code changes.
2. For a product migration, resolve the configured Selenium source from `migration/state/source-scope.json`; if it is missing, stop and report `SOURCE_SCOPE_MISSING` instead of guessing or offering an unrelated task menu.
3. Run an optional representative `pilot` once for calibration.
4. Run the complete source through `selenium-pw-migrator run`.
5. Run a real matching `verify-project` when a target project/toolchain is available.
6. Fix one highest-payoff root cause at a time and rerun the complete standard flow.
7. Do not stop after routine POM/config analysis to ask whether to continue. When one safe, agent-executable remediation is available under `migration/**`, perform it in the same invocation, rerun the complete standard flow, and then report the result. Ask only for a human product decision or explicit authorization to write outside the migration workspace.

## Hard rules

1. Do not edit source/product files during a migration-artifact run unless explicitly authorized.
2. Generated or proposed target/POM code belongs under `migration/**` until reviewed.
3. Do not treat low TODO count as success without syntax, quality, and project-verification evidence.
4. Do not reduce TODO by suppressing assertions, deleting actions, hiding empty tests, or inventing mappings.
5. Never create validation-result JSON manually to bypass a failed CLI command. Preserve the crash/error as evidence and report the blocker.
6. Keep changed PowerShell scripts paired with a `.sh` companion when they are distributed cross-platform.

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
