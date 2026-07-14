# Migration Progress Dashboard

This is a reference document, not a second launch procedure. The canonical guarded launch procedure remains `docs/guarded-opencode-desktop-runbook.ru.md`.

## Why it exists

A migration can be making useful progress while looking stuck from the outside. For example, a full draft may already exist while the current wave is paused to improve mappings or repair evidence. The dashboard separates those facts so users do not have to interpret `CONTINUE_REQUIRED`, JSONL ledgers, or quality-budget codes.

The first screen answers four questions:

1. Where is the migration now?
2. What has already been generated?
3. What has been accepted by the guarded process?
4. What is the harness doing next, and does the user need to intervene?

Every major metric and process stage has a `?` hint. A visible intervention badge says whether the harness can continue by itself or needs a real user decision. Technical evidence remains available in expandable sections.

## Progress model

The dashboard intentionally shows two percentages:

- **Draft coverage** — tests for which Playwright output already exists. This makes real generated work visible even while it is being improved.
- **Accepted progress** — tests belonging to waves that passed their bounded quality and safety checks.

The large process percentage is an estimate weighted toward accepted progress. It is a navigation aid, not a release-readiness claim.

## Migration stages

The process guide explains the entire loop:

1. Plan small waves.
2. Generate a bounded draft.
3. Improve mappings, TODOs, fallbacks, and scope integrity.
4. Verify quality, safety, and runtime evidence.
5. Accept the wave and continue.

The active stage is highlighted. A yellow quality state usually means “useful draft exists; clean it before scaling,” not “the migration failed.”

## Generate a snapshot

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language en
```

## Run with live refresh

Keep this command running in a separate terminal:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 `
  -Workspace migration `
  -Out dashboard/harness `
  -Language en `
  -Watch `
  -RefreshSeconds 5
```

Open `migration/dashboard/harness/index.html`. The watcher regenerates HTML/JSON/Markdown and the browser reloads the local file. No server is required. Stop it with `Ctrl+C`.

Do not run `-Watch` inside an autonomous agent command that must return; it is intended for the user's terminal. `/dashboard-harness` generates a non-blocking snapshot and prints the live command.

## Output

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

The page includes short read-only previews of up to three generated `.cs` files from the current wave. These previews may contain TODOs while the improve-quality stage is active.

## English-first i18n rule

English is the canonical UI language. Russian is a complete secondary localization. The generated page exposes the `languageSelect` control for instant switching. Machine-readable fields such as event `phase`, `action`, `status`, and gate names stay stable and language-neutral.

## Acceptance checks

- `index.html` opens directly from disk;
- the language selector switches English/Russian labels and hints;
- live mode regenerates files and emits `HARNESS_DASHBOARD_REFRESHED`;
- draft and accepted progress are shown separately;
- process-stage hints and generated-test previews are present;
- `harness-dashboard.json` remains language-neutral;
- dogfood smoke writes `evidence/harness-dashboard-smoke.md`.
