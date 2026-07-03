# Release process

This document is the release checklist for publishing `SeleniumPlaywrightMigrator` as a public preview dotnet tool and for producing the standalone agent bundle.

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

8. Wait for GitHub Actions to pass:
   - `Test fast suite`;
   - `Test CLI process suite`;
   - dotnet-tool package job;
   - agent-bundle job.

9. For release candidates, optionally run the manual `Full Validation` workflow. It runs the unfiltered test suite, `release-doctor`, dotnet-tool package smoke, and agent-bundle smoke in one end-to-end gate. The same workflow also runs nightly from `schedule`.

## Stable release checklist

Before removing the preview suffix:

- no known package-content leaks;
- `--help` and key command help are understandable without internal project context;
- package smoke passes on CI;
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
- `dry_run` - keep `true` for the first run; set to `false` only for the actual publish.

The workflow:

1. restores, builds, and tests the solution in Release;
2. packs the dotnet tool;
3. verifies `.nupkg` contents;
4. installs the package from `artifacts/nuget` into a temporary local tool manifest and runs smoke checks;
5. uploads the verified `.nupkg` as a workflow artifact;
6. publishes only when `dry_run=false`.

Required repository setup:

- add `NUGET_API_KEY` as a GitHub Actions repository or environment secret;
- protect the `nuget-production` environment if you want a final manual approval gate;
- never put API keys into `NuGet.config`, scripts, workflow inputs, or committed files.

Recommended sequence:

1. run the workflow with `dry_run=true`;
2. download/check the uploaded `.nupkg` artifact if needed;
3. run the workflow again with the same `version` and `dry_run=false`;
4. verify the package page on NuGet and test install from a clean directory.

### Local/manual publish

NuGet/GitHub package publishing should happen only after the CI package artifacts pass verification.

```bash
scripts/push-tool.sh https://api.nuget.org/v3/index.json 0.0.0-preview.1
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
dotnet tool install SeleniumPlaywrightMigrator --version 0.0.0-preview.1
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
- agent bundle smoke verifies the compiled bundle, docs/templates/schema presence, and checksum manifest integrity.
