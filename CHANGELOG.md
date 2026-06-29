# Changelog

All notable changes to Selenium Playwright AST Migrator are documented here.

The format follows the spirit of Keep a Changelog, and this project uses preview SemVer-style versions while the public API is still stabilizing.

## [Unreleased]

### Added

- Public-release hygiene guardrails for NuGet package metadata and package contents.
- Public contribution and security policy documents.
- CI packaging gates for `dotnet pack`, `.nupkg` content verification, local tool install smoke, and agent bundle smoke.
- Release-process documentation for preview/stable publishing and rollback.
- Agent bundle `MANIFEST.sha256` and `manifest.json` generation.
- Public documentation entry points for quick start, user guide, config/profile guide, agent/autopilot guide, troubleshooting, limitations, and end-to-end examples.
- Migration quality dashboard artifacts: `migration-quality-dashboard.json`, `migration-quality-dashboard.md`, and `migration-quality-tickets.md`.
- Quality analyzer for TODO categories, unsupported actions, unmapped targets, selector-evidence requirements, and suppression/POM-helper guardrails.
- Agent loop hardening: one primary kickoff prompt, stop-policy checklist, artifact-mode source-edit guardrails, and refined multi-agent handoff rules.
- Extensibility/public API docs for `ISourceFrontend`, `ITargetBackend`, `adapter-config/v1`, and mini extension examples.
- Target backend capability reports plus `--mode capabilities` for built-in source/target support matrices.
- Public launch pack: demo repository assets, screenshot walkthrough, GitHub Actions migration-pilot example, public roadmap, issue templates, and preview release notes.

### Changed

- Tightened public launch guardrails so stop-policy checklist references, selector-evidence warnings, quality guardrail IDs, and scaffold quick-start links stay visible in docs/reports.
- Package-facing wording no longer describes the CLI as an internal-only tool.
- README, README.ru, package README, and limitations now consistently label stable and experimental capabilities.
- Migration-kit prompts now delegate to the primary autopilot contract and explicitly forbid routine “continue?” prompts under `CONTINUE_AUTONOMOUSLY`.
- Adapter config now has an explicit default `SchemaVersion` of `adapter-config/v1`, and public schemas use the GitHub repository URL as `$id`.

### Removed

- Local `.agent-state` files from packaged dotnet-tool contents.

## [0.6.0-preview.1]

### Added

- Dotnet tool packaging for `selenium-pw-migrator`.
- Migration kit templates and installation scripts.
- Multi-frontend and multi-target migration architecture with Selenium C# and Playwright targets.
