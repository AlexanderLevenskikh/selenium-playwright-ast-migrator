# Standalone installation

Standalone distribution does not require the .NET SDK or .NET Runtime on the target machine. It is the recommended path for users who only need to run the CLI without installing .NET first.

The migrator is published as a self-contained runtime-specific bundle. It is intentionally not published as a single-file executable: `PublishSingleFile` is kept disabled (`PublishSingleFile=false`) because the CLI uses Roslyn and project-reference assemblies/resources that must stay next to the executable. Users still do not need .NET installed when the bundle was built with `--self-contained true`.

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
  -BaseUrl https://nexus.example/repository/migrator/releases/v0.0.0-preview.1 `
  -AddToUserPath
```

The default install location is:

```text
%USERPROFILE%\.selenium-pw-migrator\bin
```

Verify:

```powershell
selenium-pw-migrator --version
selenium-pw-migrator --help
```

If the command is not found, open a new terminal or add the install directory to `PATH` manually.

## Linux/macOS install

```bash
curl -fsSL https://raw.githubusercontent.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/main/scripts/install-standalone.sh | sh
```

For a private release directory:

```bash
VERSION=0.0.0-preview.1 \
BASE_URL=https://nexus.example/repository/migrator/releases/v0.0.0-preview.1 \
sh ./scripts/install-standalone.sh
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

Windows:

```powershell
Remove-Item -Recurse -Force "$HOME/.selenium-pw-migrator"
```

Linux/macOS:

```bash
rm -rf ~/.selenium-pw-migrator
```

Also remove the install directory from `PATH` if it was added manually.

## When to use dotnet tool instead

Use `dotnet tool` when a project wants to pin the CLI in `.config/dotnet-tools.json` or when all users already have the .NET SDK installed. See [Tool installation](tool-installation.md).
