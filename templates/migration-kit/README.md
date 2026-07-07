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
  harness/
    README.md
  dashboard/
    i18n/
      en.json
      ru.json
  .migration-kit/
    version.json
    updates/
    backups/
```


## First OpenCode agent start

Do not manually create run folders. The same workspace supports OpenCode Desktop, OpenCode CLI, Codex, CI, and other agents; see `docs/agent-environments.md` in the source repository for the environment matrix. From the product repository root, prefer the one-command bootstrap; it installs or updates the kit, includes OpenCode team files, runs `kit doctor`, and installs the OpenCode Desktop ProjectDesktop config:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source . --config migration/profiles/adapter-config.json --opencode-install auto
```

Manual fallback:

```powershell
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source . --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

Then open the product repo root in OpenCode Desktop and run `/supervised-task`. The orchestrator creates or resumes the active run with `scripts/new-harness-run.ps1`.

## First run

For OpenCode Desktop, follow `docs/guarded-opencode-desktop-runbook.ru.md` instead of copying prompts manually.

Manual/non-Desktop fallback:

1. Edit `profiles/adapter-config.json` or replace it with your project config.
2. Copy `prompts/kickoff-prompt.txt` into your agent.
3. Run only a bounded artifact-only batch.
4. After every batch, keep `agent-state.md`, `current-ticket.md`, and `state/*` up to date.
5. Run gates before accepting any batch. Bash users can run `scripts/check-harness-policy.sh`, `scripts/check-scope.sh`, and `scripts/check-final-gate.sh`; PowerShell users can call the matching `.ps1` scripts directly.

## Updating the kit

The tool/bundle is disposable. This workspace is persistent.

Safe update:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup
```

Update rules:

- project-owned files are not overwritten: config, mutable state, current ticket, runs, reports, logs;
- runtime kit files are updated in place: guard scripts, shell wrappers, harness prompts, and `state/continuation-contract.md`;
- changed project-owned or mutable files are written under `.migration-kit/updates/<timestamp>/*.new`;
- `-Force` overwrites non-mutable kit-owned files when a manual full refresh is intentional;
- `-Backup` snapshots the workspace before update.

## MVP-4 agent harness

Use `scripts/new-harness-run.ps1` to create a resumable run workspace under `runs/<run-id>/` with `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.

Use `state/harness-policy.json` as the machine-readable autopilot policy. `scripts/check-harness-policy.ps1` verifies policy presence, active run files, OpenCode edit policy, and guard-sensitive changes. Guard-sensitive kit files changed by a trusted `kit update` are accepted only when the changed guard scripts match `.migration-kit/guard-checksums.json`; checksum mismatches still fail. Metadata-only checksum timestamp churn is ignored when all guard file hashes still match the checksum baseline.

Public docs are English-first. Russian docs are secondary localization (`*.ru.md`). Machine-readable event/status codes stay language-neutral.

Repository-level Harness Kit dogfood is documented in `docs/migrator-agent-harness-dogfood.md`. Use `scripts/run-harness-dogfood-smoke.ps1` from the Migrator repository root to validate install/run/event/policy behavior in a temporary `.dogfood/migration` workspace.

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

## Harness dashboard

Use `scripts/build-harness-dashboard.ps1` to create a static dashboard from the active harness run:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language en
```

The dashboard is English-first and includes a `languageSelect` control for Russian. Machine-readable event/status codes stay language-neutral.


Windows OpenCode Desktop shortcut: `--project-desktop` remains an alias for `--opencode-install project-desktop`.


## Harness continuation strict protocol

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. After `FINAL`, stop for review and report evidence; start another run only on explicit `continue` or bounded auto-continuation. Plain `continue` enters the closed post-final loop: researcher → research lead → task slicer → one bounded executor task when allowed. Stop for guard/scope/policy blocker, missing input, loop/plateau, or max autonomous budget.

## Project-scoped migration memory

The kit includes `state/memory/**` as an inspectable project-local memory. Agents should read `state/memory/memory-summary.md` before planning, record durable decisions/warnings/final-gate lessons after bounded actions, and run `selenium-pw-migrator memory doctor --workspace migration` before final-gate handoff when the CLI is available. Memory is guidance, not authority: it cannot justify assertion suppression, over-suppressed user interactions, or selectors without evidence.
