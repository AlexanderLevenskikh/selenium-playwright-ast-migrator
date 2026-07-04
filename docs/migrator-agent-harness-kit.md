# Migrator Agent Harness Kit

This is a reference document, not a second launch procedure. The canonical guarded OpenCode Desktop launch procedure remains `docs/guarded-opencode-desktop-runbook.ru.md`.

## Purpose

The kit turns a migration run into a controlled file-based workflow:

1. A machine-readable policy says what the agent can do automatically and what is denied.
2. A run bootstrapper creates `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and trace files under `migration/runs/<run-id>/`.
3. Guard scripts verify scope, final quality, and harness configuration.
4. The agent works autonomously only inside this boundary.

## Design principle

Prompts guide behavior; scripts enforce behavior.

The agent may say it followed rules, but the final answer is not trusted until deterministic checks pass.


## Bootstrap

For cross-environment setup details, see [Agent environments](agent-environments.md). contract

The user should not manually create `migration/` subfolders or `migration/runs/<run-id>/`. The preferred OpenCode bootstrap is one command from the product repository root:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

This installs or updates the migration workspace, includes OpenCode team templates, runs `kit doctor`, and installs the project-local OpenCode Desktop config. Manual fallback:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

After that, `/supervised-task` or `/harness-run` owns the run lifecycle. The orchestrator must call `migration/scripts/new-harness-run.ps1` when no matching active run exists, then continue from the generated run files.

So the answer to “can the agent start from zero?” is: after the tool and project-local OpenCode config are bootstrapped, yes for migration workspace lifecycle and run artifacts. The first OpenCode config installation remains a one-time bootstrap step because the agent cannot use project-local roles before they exist.

## Minimal lifecycle

```text
new-harness-run.ps1
  -> creates run artifacts
  -> updates agent-state/current-ticket/run-ledger/handoff
agent reads autopilot-loop-prompt.txt
  -> works inside migration/**
  -> records trace events
  -> runs scope/final/policy checks
check-final-gate.ps1
  -> decides PASS/FAIL for final claims
```

## Autopilot rule

The agent should not ask the user for permission to do actions already allowed by `state/harness-policy.json` and the OpenCode permission configuration.

It must stop with a concrete blocker for:

- writes outside allowed roots;
- edits to guard scripts/checksums/permissions;
- package installs or dependency upgrades;
- network access;
- git commit/push/reset/clean;
- destructive delete/move operations;
- changes to real product/POM/Playwright project files in artifact-only mode.

## Files

```text
migration/
  AGENT_CONTRACT.md
  agent-state.md
  current-ticket.md
  state/
    harness-policy.json
    harness-run.json
    harness-events.jsonl
    harness-policy-result.md/json
    run-ledger.md
    handoff.md
    final-gate.md
  runs/
    run-001/
      Prompt.md
      Plan.md
      Implement.md
      Documentation.md
      trace.jsonl
```

## Why this helps

Without this harness, the agent behaves like a nervous junior developer asking about every shell command.

With the harness, the allowed lane is explicit: read/search/build/test/migrate/write migration artifacts. The dangerous lane is also explicit: real project edits, guardrail edits, git push, dependency changes, secrets, network.

## Dogfood smoke

Use `docs/migrator-agent-harness-dogfood.md` and `scripts/run-harness-dogfood-smoke.ps1` for the first repository-level validation pass. The smoke installs the kit into `.dogfood/migration`, creates a run, writes events, and verifies `check-harness-policy.ps1` with explicit dogfood allowed roots.

## English-first and dashboard i18n

English is canonical for public Harness Kit docs, prompts, report labels, event codes, and dashboard terminology.

Russian is supported as a secondary localization through `*.ru.md` docs and dashboard dictionaries such as `en.json` / `ru.json`.

Machine-readable data must stay language-neutral. Store stable English codes such as `final-gate-pass`, `scope-guard-failed`, or `harness-policy-pass`; localize only UI labels and documentation.

Future dashboard work should default to English and provide a language switch:

```text
Language: English / Русский
```


## Harness dashboard

Use `docs/migrator-agent-harness-dashboard.md` and `scripts/run-harness-dashboard-smoke.ps1` to generate a static dashboard from the active harness run.

The installed workspace contains:

```text
migration/dashboard/
  i18n/
    en.json
    ru.json
  harness/
    index.html
    harness-dashboard.json
    harness-dashboard.md
```

English is the default dashboard language. Russian is available through the `languageSelect` switch. Dashboard JSON remains language-neutral.


Windows OpenCode Desktop shortcut: `--project-desktop` remains an alias for `--opencode-install project-desktop`.
