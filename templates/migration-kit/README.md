# Migration workspace

This folder is the local workspace for a Selenium C# → Playwright migration.

It is intentionally file-based so an AI agent can resume after context loss.

## Layout

```text
migration/
  README.md
  .gitignore
  agent-state.md
  current-ticket.md
  profiles/
    adapter-config.json
  prompts/
    kickoff-prompt.txt
    resume-prompt.txt
    loop-batch-prompt.txt
    review-batch-prompt.txt
    next-ticket-prompt.txt
  state/
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

## First run

1. Edit `profiles/adapter-config.json` or replace it with your project config.
2. Copy `prompts/kickoff-prompt.txt` into your agent.
3. Let the agent run the smallest safe migration loop.
4. After every batch, keep `agent-state.md`, `current-ticket.md`, and `state/*` up to date.

## Updating the kit

The tool/bundle is disposable. This workspace is persistent.

Safe update:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup
```

Update rules:

- project-owned files are not overwritten: config, state, current ticket, runs, reports, logs;
- changed kit-owned files are written under `.migration-kit/updates/<timestamp>/*.new`;
- `-Force` overwrites kit-owned files;
- `-Backup` snapshots the workspace before update.

## MVP-2 stateful loop

Use `prompts/loop-batch-prompt.txt` for one bounded batch.
Use `prompts/review-batch-prompt.txt` to review the last batch.
A new agent should start from `state/handoff.md`, not from chat memory.

## Safety rules

- Do not edit generated files as the final solution.
- Prefer config fixes over engine fixes when project-specific knowledge is needed.
- Never suppress assertions silently.
- Compile-green is a checkpoint, not the end of migration quality work.
- Keep source truth in source tests, POM/helper code, config, or existing target Playwright code.

## Optional MVP-3 helpers

Codex handoff files are installed under `codex/` by default. Use them for one bounded ticket at a time:

```text
Read migration/codex/CODEX.md and migration/codex/prompts/ticket-fix-prompt.txt.
Fix only the current ticket.
```

OpenCode team files are optional and can be installed with:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup -WithTeam
```

Reusable loop templates can be installed with:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup -WithLoopLibrary
```
