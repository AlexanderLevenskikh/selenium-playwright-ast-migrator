# Migration Kit MVP-1 / MVP-2

The Migration Kit packages the migrator workflow as an installable starter kit for project teams.

Goal: a team should be able to create and update a migration workspace without learning the whole migrator repository layout.

## MVP-1: installable workspace

The installer creates a local workspace, by default `migration/`:

```text
migration/
  README.md
  QUICKSTART.md
  .gitignore
  agent-state.md
  current-ticket.md
  profiles/
    adapter-config.json
  prompts/
    kickoff-prompt.txt
    resume-prompt.txt
    next-ticket-prompt.txt
    loop-batch-prompt.txt
    review-batch-prompt.txt
  state/
    README.md
    run-ledger.md
    decision-log.md
    handoff.md
    safety-checklist.md
  tickets/
  evidence/
  runs/
  reports/
  logs/
  .migration-kit/
    version.json
    updates/
    backups/
```

It also copies root-level `.agent-loops/` unless `-NoRootAgentFiles` is used. Local `.agent-state/` is treated as runtime state and is not copied from the packaged tool.

## Install from the migrator repository

Run from the target project repository root:

```powershell
C:\path\to\migrator\scripts\install-migration-kit.ps1 `
  -Workspace migration `
  -Source "C:\path\to\selenium-tests" `
  -Target "C:\path\to\target-playwright-or-output" `
  -Config "migration\profiles\adapter-config.json" `
  -Output "migration\runs\run-001"
```

## Install from an agent CLI bundle

If the migrator was published with `scripts/package-agent-cli-bundle.ps1`, use:

```powershell
.\tool\scripts\install-migration-kit.ps1 `
  -Workspace migration `
  -Source "C:\path\to\selenium-tests" `
  -Config "migration\profiles\adapter-config.json" `
  -Output "migration\runs\run-001" `
  -ToolCommand ".\tool\migrator.exe"
```

## Updating the kit safely

The tool/bundle is disposable. The migration workspace is persistent.

Safe update:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup
```

Update behavior:

- creates `migration/.migration-kit/version.json`;
- project-owned files are not overwritten: `profiles/adapter-config.json`, `agent-state.md`, `current-ticket.md`, `runs/`, `reports/`, `logs/`, mutable state ledgers;
- changed kit-owned files are written to `migration/.migration-kit/updates/<timestamp>/*.new` unless `-Force` is used;
- `-Backup` snapshots the current workspace and root agent files to `migration/.migration-kit/backups/<timestamp>/`;
- `-Force` overwrites kit-owned files, but still does not overwrite files explicitly marked project-owned by the installer.

## MVP-2: stateful loop

MVP-2 makes the loop resilient to context loss.

The agent must update these files after every batch:

- `migration/agent-state.md`
- `migration/current-ticket.md`
- `migration/state/run-ledger.md`
- `migration/state/decision-log.md`
- `migration/state/handoff.md`

Use prompts:

```text
migration/prompts/loop-batch-prompt.txt
migration/prompts/review-batch-prompt.txt
```

### Why this works

The loop does not depend on agent memory. It depends on files:

- `state/handoff.md` tells the next agent where to resume;
- `state/run-ledger.md` records commands and metrics;
- `state/decision-log.md` records decisions future agents must not undo casually;
- `state/safety-checklist.md` prevents false-green migration shortcuts;
- `runs/` stores immutable verify/orchestrate outputs.

A green project verify is a checkpoint. The migration continues while the board still has actionable categories.

## Boundaries

The kit intentionally avoids mandatory multi-agent/team setup.

Team mode can be layered later through `.opencode/`, but the default path should stay:

```text
install/update → doctor → kickoff/resume prompt → one batch → board → next ticket
```
