# /dashboard-harness

Generate a non-blocking human-friendly Migration Progress snapshot from the active run.

Required behavior:

1. Read `migration/state/harness-run.json`, continuation, current-ticket, wave plan/status, final gate, and wave-quality-budget state.
2. Run `migration/scripts/build-harness-dashboard.ps1` without `-Watch` so this agent command returns.
3. Keep dashboard data language-neutral. English is canonical; Russian is available through the language switch.
4. Show draft coverage separately from accepted progress, explain the current process stage, expose `?` hints, and include read-only generated-test previews when available.
5. Do not edit source product files.
6. In the final response, show the output path and the separate user-terminal command for live refresh.

Snapshot command:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language ru
```

Live command for the user's separate terminal:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language ru -Watch -RefreshSeconds 5
```

Acceptance evidence:

- `migration/dashboard/harness/index.html` exists;
- `migration/dashboard/harness/harness-dashboard.json` exists;
- `migration/dashboard/i18n/en.json` and `migration/dashboard/i18n/ru.json` exist;
- HTML contains `languageSelect`, `processGuide`, `data-hint`, `draftCoveragePercent`, `acceptedPercent`, and `previewDetails`;
- live support contains `HARNESS_DASHBOARD_REFRESHED` and self-reload behavior.
