# /dashboard-harness

Generate the Migrator Agent Harness dashboard from the active run.

Required behavior:

1. Read `migration/state/harness-run.json`.
2. Run `migration/scripts/build-harness-dashboard.ps1`.
3. Keep dashboard data language-neutral. English is the default UI language; Russian is available through the language switch.
4. Do not edit source project files.

Suggested command:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language en
```

Acceptance evidence:

- `migration/dashboard/harness/index.html` exists.
- `migration/dashboard/harness/harness-dashboard.json` exists.
- `migration/dashboard/i18n/en.json` and `migration/dashboard/i18n/ru.json` exist.
- The HTML contains the `languageSelect` control.
