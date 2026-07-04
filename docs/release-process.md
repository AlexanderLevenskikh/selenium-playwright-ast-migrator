# Release process

This document is the release checklist for publishing `SeleniumPlaywrightMigrator` as a public preview dotnet tool and for producing standalone CLI release archives.

## Versioning

Use SemVer with preview suffixes while the CLI/config surface is still changing:

```text
0.0.0-preview.1
0.0.0-preview.1
0.0.0-preview.N
```

Publish a stable version only when the public command set, package contents, and config schema are intentionally stable enough for external users.

## Preview release checklist

1. Update `CHANGELOG.md` with user-visible changes.
2. Verify package metadata in `Migrator.Cli/Migrator.Cli.csproj`:
   - `PackageId`
   - `Version`
   - `PackageProjectUrl`
   - `RepositoryUrl`
   - `PackageLicenseExpression`
   - `PackageReadmeFile`
   - `PackageIcon`

   Or run the release doctor from the repository root:

```bash
selenium-pw-migrator doctor release --out release-doctor --format both
```

   The report checks package metadata, version/changelog consistency, release scripts, README_TOOL packaging docs, publish workflow dry-run support, NuGet secret references, and repository hygiene.
3. Run the normal build/test gate:

```bash
dotnet restore Migrator.sln
dotnet build Migrator.sln --configuration Release --no-restore
dotnet test Migrator.sln --configuration Release --no-build
```

4. Pack the dotnet tool:

```bash
scripts/pack-tool.sh 0.0.0-preview.1
```

or on Windows:

```powershell
./scripts/pack-tool.ps1 -Version 0.0.0-preview.1
```

5. Verify `.nupkg` contents:

```bash
scripts/verify-nupkg-contents.sh artifacts/nuget/SeleniumPlaywrightMigrator.0.0.0-preview.1.nupkg
```

or on Windows:

```powershell
./scripts/verify-nupkg-contents.ps1 -PackagePath artifacts/nuget/SeleniumPlaywrightMigrator.0.0.0-preview.1.nupkg
```

6. Smoke local installation from the package:

```bash
scripts/smoke-local-tool-package.sh 0.0.0-preview.1
```

or on Windows:

```powershell
./scripts/smoke-local-tool-package.ps1 -Version 0.0.0-preview.1
```

The smoke installs the package into a temporary local tool manifest, runs `--help`, runs `--mode doctor`, and checks that a doctor report was written.

7. Build and verify the agent bundle:

```powershell
./scripts/package-agent-cli-bundle.ps1 -Runtime win-x64 -Output artifacts/agent-cli-bundle
./scripts/verify-agent-cli-bundle.ps1 -BundleDirectory artifacts/agent-cli-bundle/tool -RunHelp
```

For Linux CI smoke, use a framework-dependent bundle:

```powershell
./scripts/package-agent-cli-bundle.ps1 -Runtime linux-x64 -Output artifacts/agent-cli-bundle -NoSelfContained
./scripts/verify-agent-cli-bundle.ps1 -BundleDirectory artifacts/agent-cli-bundle/tool -RunHelp
```

8. Build standalone release archives:

```powershell
./scripts/package-standalone.ps1 `
  -Version 0.0.0-preview.1
```

The release directory must contain the versioned and latest-alias archives for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`, plus `checksums.sha256` and `standalone-release-manifest.json`.

Verify the complete release artifact set before uploading it:

```powershell
./scripts/verify-release-artifacts.ps1 `
  -Version 0.0.0-preview.1
```

9. Verify the local Windows archive when running on Windows:

```powershell
./scripts/verify-standalone-package.ps1 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.1-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256 `
  -RunHelp
```

10. Wait for GitHub Actions to pass:
   - `Test fast suite`;
   - `Test CLI process suite`;
   - dotnet-tool package job;
   - standalone-release job;
   - agent-bundle job.

11. For release candidates, optionally run the manual `Full Validation` workflow. It runs the unfiltered test suite, `release-doctor`, dotnet-tool package smoke, standalone archive smoke, and agent-bundle smoke in one end-to-end gate. The same workflow also runs nightly from `schedule`.

## Stable release checklist

Before removing the preview suffix:

- no known package-content leaks;
- `--help` and key command help are understandable without internal project context;
- package smoke passes on CI;
- standalone CLI release archives have `checksums.sha256` and `standalone-release-manifest.json`;
- standalone agent bundle has `MANIFEST.sha256` and `manifest.json`;
- docs describe stable, preview, and experimental commands consistently;
- the public roadmap states what remains experimental.

## Publishing

### Recommended manual GitHub Actions publish

The repository has a manual workflow for release publishing:

```text
.github/workflows/publish-nuget.yml
```

Run it from GitHub Actions with `workflow_dispatch` inputs:

- `version` - the exact package version, for example `0.0.0-preview.1`;
- `source` - usually `https://api.nuget.org/v3/index.json`;
- `dry_run` - keep `true` for the first run; set to `false` only for the actual publish;
- `create_github_release` - keep `true` to create or update the GitHub release after NuGet publish.

The workflow:

1. restores, builds, and tests the solution in Release;
2. packs the dotnet tool;
3. verifies `.nupkg` contents;
4. installs the package from `artifacts/nuget` into a temporary local tool manifest and runs smoke checks;
5. builds standalone CLI archives for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`;
6. stages one flat GitHub release asset directory at `artifacts/github-release`;
7. verifies the staged GitHub release assets before upload;
8. uploads the staged `.nupkg`, standalone archives, `checksums.sha256`, `standalone-release-manifest.json`, and standalone install scripts as one workflow artifact;
9. publishes only when `dry_run=false`;
10. downloads and verifies the same flat release asset directory in the publish job;
11. creates or updates the GitHub release `v<version>` after a successful publish, using `docs/release-notes/v<version>.md` first and falling back to the matching `CHANGELOG.md` section;
12. attaches every file from `artifacts/github-release` to the GitHub release.

The staging directory is intentionally flat. Do not upload mixed source paths directly to GitHub Releases from `actions/download-artifact`; nested artifact layouts can make the install scripts appear in a release while the standalone archives are left behind.

Required repository setup for NuGet Trusted Publishing:

- create a nuget.org Trusted Publishing policy for this repository and the `publish-nuget.yml` workflow;
- set the policy environment to `nuget-production`;
- add `NUGET_USER` as a GitHub Actions repository or environment secret with the nuget.org profile name;
- protect the `nuget-production` environment if you want a final manual approval gate;
- never put API keys into `NuGet.config`, scripts, workflow inputs, or committed files.

Recommended sequence:

1. run the workflow with `dry_run=true`;
2. download/check the uploaded `.nupkg` and standalone release artifacts if needed;
3. run the workflow again with the same `version` and `dry_run=false`;
4. verify the package page on NuGet, the GitHub release page, release assets, checksums, and test install from a clean directory.

After install, capture `selenium-pw-migrator --version` in the smoke notes. The output should include `distribution`, `runtime`, `self-contained`, `publish-single-file`, `framework`, and, when available, `commit`/`build`.

### Internal Nexus/static mirror

For internal distribution, copy the complete standalone release directory to one flat Nexus/raw/static directory. Keep `checksums.sha256` next to the archives so install scripts can verify downloads.

Expected layout:

```text
<base-url>/
  selenium-pw-migrator-<version>-win-x64.zip
  selenium-pw-migrator-<version>-linux-x64.tar.gz
  selenium-pw-migrator-<version>-osx-x64.tar.gz
  selenium-pw-migrator-<version>-osx-arm64.tar.gz
  checksums.sha256
  standalone-release-manifest.json
  install-standalone.ps1
  install-standalone.sh
```

Windows smoke from Nexus/static mirror:

```powershell
./scripts/install-standalone.ps1 `
  -Version 0.0.0-preview.1 `
  -BaseUrl https://nexus.example/repository/migrator/releases/v0.0.0-preview.1
```

Linux/macOS smoke from Nexus/static mirror:

```bash
./scripts/install-standalone.sh \
  --version 0.0.0-preview.1 \
  --base-url https://nexus.example/repository/migrator/releases/v0.0.0-preview.1
```

### Local/manual publish

NuGet/GitHub package publishing should happen only after the CI package artifacts pass verification.
For local/manual publication to nuget.org, set `NUGET_API_KEY` in the shell first.

```bash
NUGET_API_KEY=<api-key> scripts/push-tool.sh https://api.nuget.org/v3/index.json 0.0.0-preview.1
```

or on Windows:

```powershell
./scripts/push-tool.ps1 `
  -Version 0.0.0-preview.1 `
  -Source https://api.nuget.org/v3/index.json `
  -ApiKey $env:NUGET_API_KEY `
  -SkipDuplicate
```

Use repository secrets for API keys. Never commit `NuGet.config` with credentials.

After publishing to NuGet, verify install from a clean directory:

```bash
mkdir /tmp/migrator-tool-smoke
cd /tmp/migrator-tool-smoke
dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator --prerelease
dotnet tool run selenium-pw-migrator -- --help
```

## Rollback

NuGet packages are immutable. If a package is broken:

1. Deprecate the bad version in NuGet/GitHub release notes.
2. Publish a new patch/preview version with the fix.
3. Update docs and examples to point to the fixed version.
4. If the agent bundle is affected, rebuild it and publish a corrected archive with a new checksum manifest.

For project-local tools, tell users to pin the previous good version:

```bash
dotnet tool update SeleniumPlaywrightMigrator --version 0.0.0-preview.1
```

## CI release gates

The default CI intentionally verifies more than compilation:

- `dotnet pack` catches broken package metadata and missing package assets;
- `.nupkg` content verification catches private/local artifacts before publication;
- local tool smoke verifies that installation and CLI startup work from the package, not just from source;
- standalone archive smoke verifies the release archive, checksum entry, startup, and help output;
- agent bundle smoke verifies the compiled bundle, docs/templates/schema presence, and checksum manifest integrity.
