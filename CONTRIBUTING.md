# Contributing

Thanks for helping improve Selenium Playwright Migrator. The project is still preview-stage, so small, well-scoped changes with regression tests are especially valuable.

## Development setup

1. Install .NET 10 SDK.
2. Restore and build the solution.
3. Run the test suite before opening a pull request.

```bash
dotnet restore
dotnet build Migrator.sln
dotnet test Migrator.sln
```

Validate repository script syntax before changing kit or packaging scripts:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./scripts/validate-scripts.ps1 -Root . -RequireShell
```

The script intentionally checks source-of-truth locations (`scripts/**`, `templates/**`, `.github/workflows/**`) and skips generated copies under `artifacts/**`, `.dogfood/**`, and `npm/native/**` by default.

## Good contribution shape

- Add a focused regression test for each migration bug.
- Keep public CLI behavior documented.
- Prefer config/profile improvements when a project-specific pattern does not need engine changes.
- Avoid committing generated migration runs, local profiles, `.agent-state`, package artifacts, logs, screenshots, or private project data.
- Keep lifecycle scripts paired: new or changed repository `scripts/*.ps1` and migration-kit `templates/migration-kit/**/*.ps1` scripts must have same-name `.sh` companions. Thin Bash wrappers that delegate to `pwsh` are fine when PowerShell remains the implementation source of truth; they must print a clear PowerShell 7 install hint for macOS/Linux/WSL (`https://learn.microsoft.com/powershell/scripting/install/installing-powershell`) and only use Windows PowerShell as a Windows-like Bash fallback.

## Pull request checklist

- Tests pass locally or the failing environment is documented.
- New public behavior is reflected in README/docs.
- Package-facing files contain no private paths, internal hosts, secrets, or local agent state.
- Generated code changes include the source pattern that motivated them.

Agent-runtime changes must preserve durable recovery invariants: an active role has a bounded lease, freshness is based on the latest heartbeat, lease/journal mutations are serialized, stale closure is an appended terminal event, derived ledger heads may be rebuilt from a valid journal, and malformed append-only role evidence is never rewritten automatically. Run `scripts/run-agent-recovery-smoke.ps1` (or `.sh`) when changing routing, role receipts, or recovery policy.
