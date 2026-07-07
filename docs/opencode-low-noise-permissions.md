# OpenCode low-noise permissions

This profile is tuned for migration-artifact autopilot runs where routine repository inspection should not interrupt the user.

## Intent

The agent should not ask for permission for routine read-only diagnostics, including:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
dotnet tool list --global
dotnet tool list --local
npm list -g selenium-pw-migrator --depth=0
git status --short --untracked-files=all
git diff --stat
git diff
Get-ChildItem migration/runs -ErrorAction SilentlyContinue
Get-Content migration/state/harness-run.json
Select-String -Path migration/**/*.md -Pattern TODO
```

```bash
command -v selenium-pw-migrator
git status --short --untracked-files=all
git diff --stat
git diff
rg "TODO|requiresReview" migration
```

The profile explicitly allows OpenCode read/navigation tools:

- `read`
- `glob`
- `grep`
- `list`
- `lsp`
- `todowrite`

It also allows routine bash/PowerShell inspection commands such as `git status*`, `git diff*`, `git show*`, `git log*`, `git ls-files*`, `Get-ChildItem*`, `Get-Content*`, `Test-Path*`, `Select-String*`, and `rg *`.

## Known migration subagents

The orchestrator may call known migration subagents without asking each time:

- `executor`
- `watchdog`
- `reviewer`
- `migration-researcher`
- `migration-research-lead`
- `migration-task-slicer`
- `migration-change-reviewer`

The `general` subagent remains denied because the migration harness expects named roles with scoped responsibilities.

## Safety boundary

The low-noise profile is not a replacement for scope guards. It reduces interactive prompts, while these checks remain mandatory:

```powershell
migration/scripts/check-scope.ps1
migration/scripts/check-harness-policy.ps1
migration/scripts/check-final-gate.ps1
```

Default allowed edits are still limited to `migration/**`. Guard scripts, checksum manifests, OpenCode permissions, and `AGENTS.md` remain protected.

Do not weaken the profile during a migration run. If a task requires broader writes, write a proposal under `migration/proposals/**` and stop with a blocker.


## Optional TrustedProject mode

For local dogfood runs, maintainers can opt into `TrustedProject` permissions. That profile allows `edit`, `bash`, and known subagents inside the project directory to eliminate approval noise, while keeping `external_directory: deny`.

See `docs/opencode-trusted-project-permissions.md`.
