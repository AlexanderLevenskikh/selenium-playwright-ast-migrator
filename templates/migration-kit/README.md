# Migration workspace

For the current guarded OpenCode Desktop launch procedure, use the canonical runbook in the migrator repository:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

This README describes the installed `migration/` workspace layout and local rules.

This folder is the local workspace for a Selenium C# → Playwright migration.

It is intentionally file-based so an AI agent can resume after context loss.

## Layout

## Agent skills

Reusable behavior contracts live under `agent-skills/`:

Applied skills are recorded with `scripts/record-agent-skill-profile.ps1` / `.sh` for common role profiles, or `scripts/write-agent-skill-usage.ps1` / `.sh` for custom one-off decisions, into `state/agent-skill-usage.jsonl` and `runs/<run-id>/skills/applied-skills.md`; final gate checks this evidence in skill-enabled workspaces.


- `agent-skills/skill-map.md` tells each role which skill to load.
- `agent-skills/plow-ahead/SKILL.md` keeps bounded autopilot work moving through routine ambiguity.
- `agent-skills/read-the-damn-docs/SKILL.md` prevents dependency/API work from relying on stale model memory.
- `agent-skills/agent-watchdog/SKILL.md` audits another agent's claims against real evidence.

Skills are instructions, not permissions. `AGENT_CONTRACT.md`, `harness-policy.json`, OpenCode policy, scope guard, and final gate still win.


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
  agent-skills/
    skill-map.md
    */SKILL.md
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

Do not manually create run folders. The same workspace supports OpenCode Desktop, OpenCode CLI, Codex, CI, and other agents; see `docs/agent-environments.md` in the source repository for the environment matrix. From the product repository root, prefer the one-command bootstrap; it installs or updates the kit, includes OpenCode team files, applies the repository-root `opencode.jsonc`/`.opencode` command pack, runs `kit doctor`, and optionally installs an OpenCode launcher config:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source . --config migration/profiles/adapter-config.json --opencode-install auto
```

Manual fallback:

```powershell
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source . --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
.\migration\scripts\apply-opencode-project-config.ps1 -RepoRoot . -Workspace migration
```

Bash/WSL fallback:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source . --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
./migration/scripts/apply-opencode-project-config.sh --repo-root . --workspace migration
```

`bootstrap-opencode` normally copies `opencode.jsonc`, `.opencode/agents/*`, `.opencode/commands/*`, and `AGENTS.md` into the repository root with backups under `migration/.migration-kit/opencode-backups/`. The apply script is a repair fallback for old/skipped workspaces. Then open the product repo root in OpenCode Desktop and run `/supervised-task waves` for a fresh divide-and-conquer start. The orchestrator creates or resumes the active run with `scripts/new-harness-run.ps1`. Do not start kit/wave commands from `Web/**` or another source/target subdirectory; a nested `Web/**/migration/**` workspace is a process defect and is blocked by doctor/final-gate/sentinel checks.

## First run

For OpenCode Desktop, follow `docs/guarded-opencode-desktop-runbook.ru.md` instead of copying prompts manually.

Manual/non-Desktop fallback:

1. Edit `profiles/adapter-config.json` or replace it with your project config.
2. Copy `prompts/kickoff-prompt.txt` into your agent.
3. Run only a bounded artifact-only batch.
4. After every batch, keep `agent-state.md`, `current-ticket.md`, and `state/*` up to date.
5. Run gates before accepting any batch. Bash users can run `scripts/check-harness-policy.sh`, `scripts/check-scope.sh`, and `scripts/check-final-gate.sh`; PowerShell users can call the matching `.ps1` scripts directly.

On macOS/Linux/WSL, the `.sh` lifecycle entrypoints are thin wrappers around the same `.ps1` scripts and require PowerShell 7 (`pwsh`). Run `selenium-pw-migrator kit doctor --workspace migration` and check `powershell-7`; install PowerShell 7 from https://learn.microsoft.com/powershell/scripting/install/installing-powershell if it is missing.

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


## Machine-state write safety

Harness state is part of the contract, not scratch text. Agents must not bypass denied OpenCode edit permissions by retrying writes through shell tools such as PowerShell `Set-Content`, `Out-File`, `tee`, `sed -i`, or redirection. A denied write is reported as `BLOCKED_BY_OPENCODE_PERMISSION_DENIED`.

JSONL ledgers are append-only by default. Use `scripts/write-harness-event.ps1`/`.sh` for harness events and traces, `selenium-pw-migrator memory add` or `scripts/write-memory-entry.ps1`/`.sh` for memory entries, and `scripts/repair-memory-jsonl.ps1`/`.sh` only for explicit invalid-JSONL repair with a backup under `state/memory/.repair-backups/`.

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

After a non-final final gate, read `migration/state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, `NOT FINAL` is not a stopping point: execute exactly one next bounded action under `migration/**` before a user-facing handoff. A fresh `FINAL` checkpoint stops once for review and reports evidence; any later `/supervised-task` where `harness-run.json` is already `FINAL_STOPPED_FOR_REVIEW` resumes the closed post-final loop automatically: researcher → research lead → task slicer → change reviewer → one bounded executor task when approved. Stop for guard/scope/policy blocker, missing input, loop/plateau, or max autonomous budget.

## Project-scoped migration memory

The kit includes `state/memory/**` as an inspectable project-local memory. Agents should read `state/memory/memory-summary.md` before planning, record durable decisions/warnings/final-gate lessons after bounded actions, and run `selenium-pw-migrator memory doctor --workspace migration` before final-gate handoff when the CLI is available. Memory is guidance, not authority: it cannot justify assertion suppression, over-suppressed user interactions, or selectors without evidence.


## Session export and sentinel

The kit includes a forensic session export path for process debugging:

```powershell
./migration/scripts/export-opencode-session.ps1 -Workspace migration -RunId run-001 -InputPath ./opencode-transcript.md
```

The generated artifact lives at `migration/runs/<run-id>/opencode-session-export.md`. When native OpenCode transcript export is unavailable, the script creates a best-effort export so `harness-sentinel` can still inspect traces, state, and observations without pretending a transcript exists.

`harness-sentinel` is the process tester. It writes `migration/runs/<run-id>/sentinel/sentinel-report.md` and records findings with `migration/scripts/write-sentinel-finding.*`. High/critical agent-executable findings are routed back into bounded hardening tasks instead of being handed to the user as vague advice. `migration/scripts/slice-gate-followups.ps1` / `.sh` turns final-gate and sentinel diagnostics into `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and `current-ticket.md` before another wave starts.


Sentinel inspections must be finalized with `migration/scripts/complete-sentinel-inspection.ps1` or `.sh`; final gate treats a missing active-run `sentinel-inspection.json` as a process defect.


Final gate reconciles `migration/state/harness-run.json` after every run: gate failure writes `BLOCKED_BY_GATE`/the concrete continuation status and real `latestChecks`; a supervisor must not continue from stale `CONTINUE_AUTONOMOUSLY` state after a failed gate.
