- Added optional OpenCode `TrustedProject` permission profile for local dogfood runs and expanded default install diagnostics allowlist.

# Changelog

All notable changes to Selenium Playwright Migrator are documented here.

This project uses preview SemVer-style versions while the public API is still stabilizing.

## [Unreleased]

### Added

- Installation diagnostics scripts and docs that start from PATH resolution before package-manager-specific checks.
- Final distribution verification scripts/checklist for repository, npm/Nexus, release, and project-pilot readiness.
- npm Trusted Publishing handoff docs plus Scoop/Homebrew package-manager templates.
- Isolated npm registry smoke scripts for validating published wrapper installs through npmjs or corporate Nexus without changing global npm state.
- Documentation for Nexus npm proxy plus internal standalone mirror post-publish smoke.

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
