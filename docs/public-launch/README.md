# Public launch pack

This folder contains the launch-facing material for the first public preview of Selenium Playwright AST Migrator.

Use it when preparing a GitHub or NuGet release, writing an announcement, or helping a new user run the tool without private project context.

## Launch assets

- [Try-it demo](../../examples/public-launch-demo/README.md) — small copyable Selenium input, adapter config, migrated Playwright output, and report examples.
- [Screenshot walkthrough](walkthrough.md) — install → doctor → migrate → verify → inspect report.
- [GitHub Actions example](../../examples/github-actions/migration-pilot.yml) — CI pilot workflow for migration artifacts.
- [Before/after report example](../../examples/public-launch-demo/reports/before-after-report.md) — reviewable migration summary for a demo test.
- [Public roadmap](../public-roadmap.md) — preview-to-stable direction.
- [Preview release notes](../release-notes/v0.6.0-preview.1.md) — copy-ready GitHub/NuGet notes.
- [Launch checklist](launch-checklist.md) — final checks before publishing.

## Launch positioning

The public preview should be presented as a **measurable migration toolkit**, not a one-click converter.

The promise:

1. run a small pilot;
2. inspect unmapped selectors and unsupported helpers;
3. improve adapter/profile mappings using real source evidence;
4. regenerate and verify;
5. repeat until the migration quality dashboard shows the next useful work.

The tool is strongest when teams already have Selenium PageObjects, helper conventions, or stable selector evidence that can be converted into project-specific profiles.
