# Migration Progress dashboard assets

This directory contains the language dictionaries used by `scripts/build-harness-dashboard.ps1`.

The dashboard is deliberately user-facing rather than a raw harness report. It explains:

- where the migration is in the wave plan;
- draft coverage versus accepted progress;
- what the harness is doing now and what it will do next;
- why a quality pause is useful rather than a failed migration;
- the five migration stages, with `?` hints for every stage and key metric;
- short read-only previews of generated Playwright .NET tests;
- safety gates, quality-budget details, and recent activity in expandable sections.

`i18n/en.json` is the canonical English UI dictionary. `i18n/ru.json` is the Russian localization. Machine-readable event/status/action codes remain language-neutral.

## Snapshot

```powershell
.\migration\scripts\build-harness-dashboard.ps1 `
  -Workspace migration `
  -Out dashboard/harness `
  -Language ru
```

## Live refresh

Keep a separate terminal open:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 `
  -Workspace migration `
  -Out dashboard/harness `
  -Language ru `
  -Watch `
  -RefreshSeconds 5
```

Then open `migration/dashboard/harness/index.html`. The script regenerates the static files and the page reloads itself, so no web server is required. Stop the watcher with `Ctrl+C`.
