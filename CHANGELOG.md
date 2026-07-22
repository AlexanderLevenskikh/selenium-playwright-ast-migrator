# Changelog

All notable changes to Selenium Playwright Migrator are documented here.

This project uses preview SemVer-style versions while the public API is still stabilizing.

## [Unreleased]

### Breaking changes

- Removed the Waves/partition runtime, its CLI command family, wave state machine, claims, leases, acceptance receipts, quality-manager/sentinel roles, dashboard lifecycle, recovery commands, and related packaging requirements. Existing historical release notes remain as history, but new workspaces no longer install or execute that mode.
- `/supervised-task` now has one ordinary full-project flow. `/supervised-task continue` may apply one bounded source-backed repair and then reruns the complete configured source scope; it never advances a hidden partition.

### Added

- Added the stable direct `selenium-pw-migrator run` entry point for the linear analyze → migrate → verify → propose pipeline.
- Added standard-run contract tests and repository smoke scripts that exercise the ordinary full-source command without wave artifacts.
- Added a strict standard final gate backed by the current orchestration report, generated report, and real matching `verify-project` report. Missing verification fails by default instead of being represented by synthetic evidence.

### Changed

- Simplified the installed OpenCode team to four roles: orchestrator, executor, reviewer, and watchdog.
- The standard full-project migration is now the only supported execution model: one configured source scope, one generated result, real project verification, and one final report.
- Kept project-scoped migration memory, reviewable config-delta merging, scope checks, artifact hygiene, no-progress detection, and project verification as optional optimizations and safeguards around the ordinary run.
- Simplified onboarding, installation, packaging, CI, docs, and handoff templates so they point to `pilot` (optional calibration), `run`, `verify-project`, and final-gate checks only.
- A failed CLI command, missing SDK, or unavailable target project is now reported as a blocker; agents are explicitly forbidden from reconstructing PASS/NOT_RUNNABLE evidence by hand.

### Fixed

- Corrected standard-run examples so `verify-project` receives the original Selenium source and matching config rather than the generated output directory.
- Corrected the generated final-gate command contract to accept `-Run` and `-RepoRoot`, and made project-verification evidence mandatory by default.

## [0.0.0-preview.8]

### Added

- npm registry distribution through the `selenium-pw-migrator` wrapper package.
- Preview dist-tag guidance: install current previews with `npm install -g selenium-pw-migrator@preview`.
- Corporate Nexus npm proxy support through npm config keys such as `selenium-pw-migrator-base-url`.
- Token-first npm publish workflow with optional Trusted Publishing/provenance mode.

### Fixed

- npm publish workflow now defaults prereleases to the `preview` dist-tag instead of `latest`.
- Publish scripts reject prerelease versions when the `latest` dist-tag is selected accidentally.
- npm wrapper source files are tracked even though generic `bin/` folders are ignored elsewhere.

### Notes

- The npm package remains a thin wrapper around standalone release archives; it does not require the .NET SDK or .NET Runtime on the target machine.
- Corporate users can install the npm package through a Nexus npm proxy and download the native standalone payload from an internal static/Nexus mirror.

## [0.0.0-preview.5]

### Added

- Standalone self-contained release archives for Windows, Linux, and macOS.
- Windows and Unix standalone installers with GitHub Release, Nexus, static `BaseUrl`, and local archive install modes.
- Verified GitHub Release asset staging with checksums, alias archives, and `standalone-release-manifest.json`.
- Rich `selenium-pw-migrator --version` diagnostics for commit, build time, distribution, runtime, self-contained mode, and `PublishSingleFile` state.

### Fixed

- GitHub Releases now attach all standalone archives, checksums, and release manifests instead of only attaching installer scripts and the NuGet package.
- Windows standalone installer adds the user-local binary directory to `PATH` by default and updates the current PowerShell session.
- Release artifact verification now fails when expected standalone assets are missing from the flat GitHub Release staging directory.

### Notes

- Standalone archives do not require the .NET SDK or .NET Runtime on the target machine.
- `PublishSingleFile` remains disabled for standalone bundles because the Roslyn-based CLI expects adjacent assemblies and bundled resources.

## [0.0.0-preview.1]

### Added

- First public preview of the Selenium C# to Playwright .NET migration path.
- Public dotnet tool packaging as `SeleniumPlaywrightMigrator` with command `selenium-pw-migrator`.
- Reports, verification gates, PR/evidence packs, playground demo, and guarded migration-kit bootstrap.
- Experimental preview support for Playwright TypeScript target output, Selenium Java source parsing, and Selenium Python source parsing.

### Notes

- The stable production path is Selenium C# -> Playwright .NET.
- Java, Python, and TypeScript paths remain experimental and must be validated with generated reports and target project checks.
