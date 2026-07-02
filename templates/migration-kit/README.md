# Migration workspace

For the current guarded OpenCode Desktop launch procedure, use the canonical runbook in the migrator repository:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

This README describes the installed `migration/` workspace layout and local rules.

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
    stop-policy-checklist.md
  tickets/
  evidence/
  proposals/
  runs/
  reports/
  logs/
  .migration-kit/
    version.json
    updates/
    backups/
```

## First run

For OpenCode Desktop, follow `docs/guarded-opencode-desktop-runbook.ru.md` instead of copying prompts manually.

Manual/non-Desktop fallback:

1. Edit `profiles/adapter-config.json` or replace it with your project config.
2. Copy `prompts/kickoff-prompt.txt` into your agent.
3. Run only a bounded artifact-only batch.
4. After every batch, keep `agent-state.md`, `current-ticket.md`, and `state/*` up to date.
5. Run `scripts/check-scope.ps1` and `scripts/check-final-gate.ps1` before accepting any batch.

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

Use `prompts/kickoff-prompt.txt` as the primary wrapper for new runs.
Use `prompts/loop-batch-prompt.txt` for one bounded batch.
Use `prompts/review-batch-prompt.txt` to review the last batch.
A new agent should start from `state/handoff.md`, not from chat memory.

## Safety rules

- Do not ask “continue?” when status is `CONTINUE_AUTONOMOUSLY`.
- Do not edit generated files as the final solution.
- Do not edit migrator source code in `migration-artifact` mode.
- Prefer config fixes over engine fixes when project-specific knowledge is needed.
- Never suppress assertions silently.
- Compile-green is a checkpoint, not the end of migration quality work.
- Keep source truth in source tests, POM/helper code, config, or existing target Playwright code.
- Fill `state/stop-policy-checklist.md` before stopping, handing off, or reporting a blocker.

## Artifact-Only Acceptance Rules

- Default writes are artifact-only: keep changes inside `migration/**`.
- Do not edit real target project files, production POMs, Playwright test project files, `.csproj`, `nuget.config`, or root-level generated files from an artifact-only run.
- When POM code is needed, create generated POM/scaffold/proposal artifacts under `migration/**`; do not apply them to the real project.
- Run `migration/scripts/check-scope.ps1` after edits and before accepting a batch.
- Never count TODO removed by assertion/business suppression as migration progress.
- `0 TODO` is not success if it was achieved by suppression, empty tests, weakened assertions, dummy known identifiers, or edits to the real target project.

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
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup
```
