# Migrator Agent Harness Dogfood

This document defines the first reproducible dogfood pass for the Migrator Agent Harness Kit.

The goal is not to prove that every migration works. The goal is to prove that the harness itself is installable, resumable, guarded, and usable by an agent without routine continuation questions.

## Dogfood target

Use a tiny docs/template-only task. A good first task is:

```text
Create or update one Harness Kit reference/evidence artifact without touching product source files.
```

Allowed roots for this dogfood pass:

```text
migration/**
docs/**
templates/migration-kit/**
templates/opencode-team/**
scripts/**
Migrator.Tests/**
```

These roots are intentionally wider than a normal product migration run because the dogfood happens inside the Migrator repository itself. Normal installed product runs remain artifact-only and should keep writes under `migration/**`.

## Smoke script

From the repository root, run:

```powershell
pwsh .\scripts\run-harness-dogfood-smoke.ps1 -Clean
```

On macOS/Linux with PowerShell installed:

```bash
./scripts/run-harness-dogfood-smoke.sh -Clean
```

The script:

1. installs the migration kit into `.dogfood/migration`;
2. creates a resumable harness run;
3. verifies required harness files;
4. runs `check-harness-policy.ps1` with explicit dogfood allowed roots;
5. writes dogfood evidence under `.dogfood/migration/evidence/`.

## Agent dogfood command

Inside OpenCode, use:

```text
/dogfood-harness
```

The command tells the orchestrator to run a small Harness Kit validation task, keep the active run artifacts current, write trace/events, and stop only on a real blocker.

## Evidence to inspect

After the dogfood smoke, inspect:

```text
.dogfood/migration/state/harness-run.json
.dogfood/migration/state/harness-events.jsonl
.dogfood/migration/state/harness-policy-result.md
.dogfood/migration/evidence/harness-dogfood-smoke.md
.dogfood/migration/runs/<run-id>/trace.jsonl
```

## Pass criteria

The dogfood pass is acceptable when:

- the kit installs without missing Harness Kit files;
- `new-harness-run.ps1` creates `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`;
- `check-harness-policy.ps1` exits with code `0`;
- at least one `dogfood-smoke` event is written to `harness-events.jsonl`;
- no routine continuation question is needed for allowed actions;
- any failure is written as evidence instead of being hidden in the chat.

## English-first rule

This document is canonical. The Russian version is a secondary localization. Machine-readable dogfood events must use stable language-neutral codes such as `dogfood-smoke-started`, `dogfood-smoke-pass`, and `dogfood-smoke-fail`.
