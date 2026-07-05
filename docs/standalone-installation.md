# Standalone installation

Standalone distribution does not require the .NET SDK or .NET Runtime on the target machine. It is the recommended path for users who only need to run the CLI without installing .NET first.

The migrator is published as a self-contained runtime-specific bundle. It is intentionally not published as a single-file executable: `PublishSingleFile` is kept disabled (`PublishSingleFile=false`) because the CLI uses Roslyn and project-reference assemblies/resources that must stay next to the executable. Users still do not need .NET installed when the bundle was built with `--self-contained true`.

## Quick install from GitHub Releases

Use this path when you want the latest standalone CLI without cloning the repository and without installing .NET.

Windows PowerShell:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
```

Version-pinned Windows install:

```powershell
$version = "0.0.0-preview.5"
$baseUrl = "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version"
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "$baseUrl/install-standalone.ps1" -OutFile $installer
& $installer -Version $version -BaseUrl $baseUrl
selenium-pw-migrator --version
```

Linux/macOS/WSL:

```bash
curl -fsSL https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.sh -o /tmp/install-standalone.sh
bash /tmp/install-standalone.sh
export PATH="$HOME/.selenium-pw-migrator/bin:$PATH"
selenium-pw-migrator --version
```

Version-pinned Linux/macOS/WSL install:

```bash
version="0.0.0-preview.5"
base_url="https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version"
curl -fsSL "$base_url/install-standalone.sh" -o /tmp/install-standalone.sh
bash /tmp/install-standalone.sh --version "$version" --base-url "$base_url"
export PATH="$HOME/.selenium-pw-migrator/bin:$PATH"
selenium-pw-migrator --version
```

## npm wrapper

Frontend-heavy teams can also install the same standalone CLI through npm:

```bash
npm install -g selenium-pw-migrator
selenium-pw-migrator --version
```

The npm wrapper downloads the matching standalone release archive during `postinstall`. It still does not require the .NET SDK or .NET Runtime. See [npm wrapper](npm-wrapper.md).

## Update

Rerun the installer with the desired version. The installer overwrites the existing files in the install directory.

Windows:

```powershell
$version = "0.0.0-preview.5"
$baseUrl = "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version"
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "$baseUrl/install-standalone.ps1" -OutFile $installer
& $installer -Version $version -BaseUrl $baseUrl
```

Linux/macOS/WSL:

```bash
version="0.0.0-preview.5"
base_url="https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version"
curl -fsSL "$base_url/install-standalone.sh" -o /tmp/install-standalone.sh
bash /tmp/install-standalone.sh --version "$version" --base-url "$base_url"
```

## Which installation is being used?

For the full diagnostic flow, use [install diagnostics](install-diagnostics.md) or run `./scripts/diagnose-install.ps1` / `scripts/diagnose-install.sh`. Do not start with `dotnet tool list` only; first inspect PATH resolution.


Windows PowerShell:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
selenium-pw-migrator --version
```

Unix-like shells:

```bash
which -a selenium-pw-migrator
selenium-pw-migrator --version
```

The first path wins. On Windows, the standalone installer adds `%USERPROFILE%\.selenium-pw-migrator\bin` before the existing user `PATH`, so standalone normally wins over `%USERPROFILE%\.dotnet\tools`.

The installer now **moves** the standalone directory to the front even if it was already present later in `PATH`. If you also have the old dotnet global tool installed and want to remove that channel in the same step, run the Windows installer with `-RemoveDotnetTool`:

```powershell
./scripts/install-standalone.ps1 `
  -Version 0.0.0-preview.8 `
  -Runtime win-x64 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.8-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256 `
  -RemoveDotnetTool
```

Manual fallback:

```powershell
dotnet tool uninstall --global SeleniumPlaywrightMigrator
```

## Release artifacts

A release contains these files:

```text
selenium-pw-migrator-<version>-win-x64.zip
selenium-pw-migrator-<version>-linux-x64.tar.gz
selenium-pw-migrator-<version>-osx-x64.tar.gz
selenium-pw-migrator-<version>-osx-arm64.tar.gz
selenium-pw-migrator-win-x64.zip
selenium-pw-migrator-linux-x64.tar.gz
selenium-pw-migrator-osx-x64.tar.gz
selenium-pw-migrator-osx-arm64.tar.gz
checksums.sha256
standalone-release-manifest.json
```

Each archive contains the executable, dependent DLL/resource files, license/security files, `README_STANDALONE.md`, and `standalone-manifest.json`.

## Private Nexus/static release directory

The install scripts do not require GitHub Releases. They also support a generic HTTP directory, for example an internal Nexus raw repository or any static file host. The directory must contain the release archives and `checksums.sha256` in one flat folder:

```text
https://nexus.example/repository/migrator/releases/v0.0.0-preview.1/
  selenium-pw-migrator-0.0.0-preview.1-win-x64.zip
  selenium-pw-migrator-0.0.0-preview.1-linux-x64.tar.gz
  selenium-pw-migrator-0.0.0-preview.1-osx-x64.tar.gz
  selenium-pw-migrator-0.0.0-preview.1-osx-arm64.tar.gz
  checksums.sha256
  standalone-release-manifest.json
  install-standalone.ps1
  install-standalone.sh
```

Use the same `-Version`/`VERSION` value that appears in the archive names. If the directory contains the unversioned alias archives such as `selenium-pw-migrator-win-x64.zip`, pass `latest` or omit the version.

## Build release archives locally

```powershell
./scripts/package-standalone.ps1 `
  -Version 0.0.0-preview.1
```

The output is written to:

```text
artifacts/release/
```

To publish only one runtime during local development:

```powershell
./scripts/publish-standalone.ps1 `
  -Runtime win-x64 `
  -Version 0.0.0-preview.1
```

## Windows install

From GitHub Releases:

```powershell
irm https://raw.githubusercontent.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/main/scripts/install-standalone.ps1 | iex
```

For a private feed or Nexus-hosted release directory, pass the base URL explicitly:

```powershell
./scripts/install-standalone.ps1 `
  -Version 0.0.0-preview.1 `
  -BaseUrl https://nexus.example/repository/migrator/releases/v0.0.0-preview.1
```

The default install location is:

```text
%USERPROFILE%\.selenium-pw-migrator\bin
```

The Windows installer adds this directory to the user `PATH` by default, moves it to the front if it was already present later, and also prepends it to the current PowerShell session. Use `-SkipUserPathUpdate` only when you want to install files without changing `PATH`.

Verify:

```powershell
selenium-pw-migrator --version
selenium-pw-migrator --help
```

Standalone `--version` output includes the distribution channel and runtime metadata:

```text
selenium-pw-migrator 0.0.0-preview.1+<commit>
commit: <commit>
build: <utc timestamp>
distribution: standalone
runtime: win-x64
self-contained: true
publish-single-file: false
framework: .NET ...
```

If the command is not found, open a new terminal and check `Get-Command selenium-pw-migrator -All`. The standalone install directory should appear before `%USERPROFILE%\.dotnet\tools` when you want standalone to win over a global dotnet tool.

## Linux/macOS install

```bash
curl -fsSL https://raw.githubusercontent.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/main/scripts/install-standalone.sh | sh
```

For a private release directory:

```bash
./scripts/install-standalone.sh \
  --version 0.0.0-preview.1 \
  --base-url https://nexus.example/repository/migrator/releases/v0.0.0-preview.1
```

For local smoke testing from a downloaded archive:

```bash
./scripts/install-standalone.sh \
  --archive-path artifacts/release/selenium-pw-migrator-0.0.0-preview.1-linux-x64.tar.gz \
  --checksums-path artifacts/release/checksums.sha256
```

The default install location is:

```text
~/.selenium-pw-migrator/bin
```

Add it to `PATH`:

```bash
export PATH="$HOME/.selenium-pw-migrator/bin:$PATH"
```

## Manual installation

1. Download the archive for your runtime.
2. Verify `checksums.sha256`.
3. Extract the full archive into a stable directory.
4. Add that directory to `PATH`.
5. Keep all files together; do not copy only the executable.

## Verify an archive

```powershell
./scripts/verify-standalone-package.ps1 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.1-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256
```

Use `-RunHelp` only for an archive that matches the current OS/runtime.

## Uninstall

Windows uninstall removes the standalone files and the standalone directory from the user `PATH`:

```powershell
$version = "0.0.0-preview.5"
$baseUrl = "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version"
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "$baseUrl/install-standalone.ps1" -OutFile $installer
& $installer -Uninstall
```

For a custom install directory:

```powershell
& $installer -Uninstall -InstallDir "C:\Tools\selenium-pw-migrator"
```

Linux/macOS uninstall removes the standalone files:

```bash
curl -fsSL https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.sh -o /tmp/install-standalone.sh
bash /tmp/install-standalone.sh --uninstall
```

Then remove `~/.selenium-pw-migrator/bin` from your shell profile if you added it there.

## When to use dotnet tool instead

Use `dotnet tool` when a project wants to pin the CLI in `.config/dotnet-tools.json` or when all users already have the .NET SDK installed. See [Tool installation](tool-installation.md).
