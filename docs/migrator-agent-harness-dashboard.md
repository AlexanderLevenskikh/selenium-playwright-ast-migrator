# Migrator Agent Harness Dashboard

This is a reference document, not a second launch procedure. The canonical guarded launch procedure remains `docs/guarded-opencode-desktop-runbook.ru.md`.

## Purpose

The dashboard turns `state/harness-events.jsonl`, `runs/<run-id>/trace.jsonl`, and `state/harness-policy-result.json` into a small static HTML report.

It is designed for dogfood and migration reviews: the agent run should be visible without reading raw JSONL files.

## Generate

From an installed migration workspace:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language en
```

From the Migrator repository dogfood flow:

```powershell
.\scripts\run-harness-dashboard-smoke.ps1 -Clean
```

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

## English-first i18n rule

English is the default and canonical dashboard language. Russian is a secondary localization.

Machine-readable dashboard data remains language-neutral: event `phase`, `action`, `status`, and policy check names are stable codes, not localized prose. Only UI labels come from `dashboard/i18n/en.json` and `dashboard/i18n/ru.json`.

## Acceptance checks

A dashboard implementation is acceptable when:

- `index.html` opens without a server;
- `languageSelect` switches English/Russian labels;
- `harness-dashboard.json` preserves language-neutral run data;
- `harness-dashboard.md` provides a quick text summary;
- dogfood smoke writes `evidence/harness-dashboard-smoke.md`.
