# npm wrapper

The npm wrapper is an optional distribution channel for frontend-heavy teams. It lets users install the Selenium Playwright Migrator with `npm install -g selenium-pw-migrator` while still running the same standalone self-contained CLI bundle used by GitHub Release assets.

The package does not embed the .NET SDK or require a local .NET installation. During `postinstall`, it downloads the matching standalone archive for the current platform and installs the native binary inside the npm package directory.

## User install

```bash
npm install -g selenium-pw-migrator
selenium-pw-migrator --version
```

The wrapper forwards all arguments and preserves the native CLI exit code:

```bash
selenium-pw-migrator kit doctor --workspace migration
```

## Supported runtimes

- `win-x64`
- `linux-x64`
- `osx-x64`
- `osx-arm64`

## Internal Nexus/static mirror

The wrapper supports the same flat release asset layout as the standalone installers:

```text
<base-url>/
  selenium-pw-migrator-<version>-win-x64.zip
  selenium-pw-migrator-<version>-linux-x64.tar.gz
  selenium-pw-migrator-<version>-osx-x64.tar.gz
  selenium-pw-migrator-<version>-osx-arm64.tar.gz
  checksums.sha256
```

Use `SELENIUM_PW_MIGRATOR_BASE_URL` when installing from an internal mirror:

```bash
SELENIUM_PW_MIGRATOR_BASE_URL=https://nexus.example/repository/migrator/releases/v0.0.0-preview.5 \
  npm install -g selenium-pw-migrator@0.0.0-preview.5
```

## Local smoke against freshly built standalone archives

After running `scripts/package-standalone.ps1`, run:

```powershell
./scripts/smoke-npm-wrapper.ps1 `
  -Version 0.0.0-preview.5 `
  -Runtime win-x64 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.5-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256
```

The smoke uses `SELENIUM_PW_MIGRATOR_ARCHIVE_PATH` and `SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH`, so it does not need a published GitHub Release.

## Packaging for npm publish

```powershell
./scripts/pack-npm-wrapper.ps1 -Version 0.0.0-preview.5
```

The script stages the wrapper under `artifacts/npm/package`, sets the npm package version, and produces a `.tgz` package with `npm pack`.
