# Release process

This document is the release checklist for publishing `SeleniumPlaywrightAstMigrator` as a public preview dotnet tool and for producing the standalone agent bundle.

## Versioning

Use SemVer with preview suffixes while the CLI/config surface is still changing:

```text
0.6.0-preview.1
0.6.0-preview.2
0.6.0-preview.N
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
3. Run the normal build/test gate:

```bash
dotnet restore Migrator.sln
dotnet build Migrator.sln --configuration Release --no-restore
dotnet test Migrator.sln --configuration Release --no-build
```

4. Pack the dotnet tool:

```bash
scripts/pack-tool.sh 0.6.0-preview.1
```

or on Windows:

```powershell
./scripts/pack-tool.ps1 -Version 0.6.0-preview.1
```

5. Verify `.nupkg` contents:

```bash
scripts/verify-nupkg-contents.sh artifacts/nuget/SeleniumPlaywrightAstMigrator.0.6.0-preview.1.nupkg
```

or on Windows:

```powershell
./scripts/verify-nupkg-contents.ps1 -PackagePath artifacts/nuget/SeleniumPlaywrightAstMigrator.0.6.0-preview.1.nupkg
```

6. Smoke local installation from the package:

```bash
scripts/smoke-local-tool-package.sh 0.6.0-preview.1
```

or on Windows:

```powershell
./scripts/smoke-local-tool-package.ps1 -Version 0.6.0-preview.1
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
   - build/test job;
   - dotnet-tool package job;
   - agent-bundle job.

## Stable release checklist

Before removing the preview suffix:

- no known package-content leaks;
- `--help` and key command help are understandable without internal project context;
- package smoke passes on CI;
- standalone agent bundle has `MANIFEST.sha256` and `manifest.json`;
- docs describe stable, preview, and experimental commands consistently;
- the public roadmap states what remains experimental.

## Publishing

NuGet/GitHub package publishing should happen only after the CI package artifacts pass verification.

```bash
scripts/push-tool.sh https://api.nuget.org/v3/index.json 0.6.0-preview.1
```

or on Windows:

```powershell
./scripts/push-tool.ps1 `
  -Version 0.6.0-preview.1 `
  -Source https://api.nuget.org/v3/index.json `
  -ApiKey $env:NUGET_API_KEY `
  -SkipDuplicate
```

Use repository secrets for API keys. Never commit `NuGet.config` with credentials.

## Rollback

NuGet packages are immutable. If a package is broken:

1. Deprecate the bad version in NuGet/GitHub release notes.
2. Publish a new patch/preview version with the fix.
3. Update docs and examples to point to the fixed version.
4. If the agent bundle is affected, rebuild it and publish a corrected archive with a new checksum manifest.

For project-local tools, tell users to pin the previous good version:

```bash
dotnet tool update SeleniumPlaywrightAstMigrator --version 0.6.0-preview.previous
```

## CI release gates

The default CI intentionally verifies more than compilation:

- `dotnet pack` catches broken package metadata and missing package assets;
- `.nupkg` content verification catches private/local artifacts before publication;
- local tool smoke verifies that installation and CLI startup work from the package, not just from source;
- agent bundle smoke verifies the compiled bundle, docs/templates/schema presence, and checksum manifest integrity.
