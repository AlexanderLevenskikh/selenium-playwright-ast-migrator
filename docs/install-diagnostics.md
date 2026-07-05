# Install diagnostics

Use this page when `selenium-pw-migrator` appears to be installed but the wrong command runs, the version does not match expectations, or an agent is trying to diagnose installation state.

## Agent rule

Do not start diagnostics with `dotnet tool list` only. The migrator can be installed through standalone archives, npm wrapper, dotnet global tool, or dotnet local tool. First identify the executable that the current shell actually resolves.

## One-command diagnostics

Windows PowerShell:

```powershell
./scripts/diagnose-install.ps1
```

Linux/macOS/WSL:

```bash
scripts/diagnose-install.sh
```

The scripts report PATH resolution, `--version` metadata, dotnet tool state, npm wrapper state, npm registry, npm prefix, and the configured `selenium-pw-migrator-base-url` used by Nexus-backed npm installs.

## Manual diagnostics

Windows PowerShell:

```powershell
Get-Command selenium-pw-migrator -All
where.exe selenium-pw-migrator
selenium-pw-migrator --version
```

Linux/macOS/WSL:

```bash
command -v selenium-pw-migrator
which -a selenium-pw-migrator || true
selenium-pw-migrator --version
```

Only after checking actual PATH resolution, inspect package-manager-specific state:

```powershell
dotnet tool list --global
dotnet tool list --local
npm list -g selenium-pw-migrator --depth=0
npm config get registry
npm config get prefix
npm config get selenium-pw-migrator-base-url
```

The same commands work in Bash without PowerShell line-continuation changes.

## How to interpret `--version`

The `--version` output includes distribution metadata:

```text
selenium-pw-migrator 0.0.0-preview.8+<commit>
commit: <commit>
build: <timestamp>
distribution: standalone
runtime: win-x64
self-contained: true
publish-single-file: false
framework: .NET 10.0.9
```

Use this metadata together with the first resolved command path:

- `%USERPROFILE%\.selenium-pw-migrator\bin` usually means standalone installer.
- `node_modules/.bin`, `%APPDATA%\npm`, or an npm global prefix usually means npm wrapper.
- `%USERPROFILE%\.dotnet\tools` usually means dotnet global tool.
- `.config/dotnet-tools.json` plus `dotnet tool run selenium-pw-migrator` usually means dotnet local tool.

If multiple commands exist, fix PATH priority before reinstalling anything.

## Common conflict: standalone vs dotnet global tool

PowerShell runs the first matching command in `PATH`. If standalone was installed but the dotnet tool still wins, make sure this directory appears before `%USERPROFILE%\.dotnet\tools`:

```text
%USERPROFILE%\.selenium-pw-migrator\bin
```

The standalone Windows installer prepends it to the user PATH by default and moves it to the front if it was already present later. Open a new terminal after installation, or run the standalone executable directly. To remove the old dotnet global tool channel, use `dotnet tool uninstall --global SeleniumPlaywrightMigrator` or reinstall standalone with `-RemoveDotnetTool`:

```powershell
& "$env:USERPROFILE\.selenium-pw-migrator\bin\selenium-pw-migrator.exe" --version
```

## Common conflict: npm wrapper behind Nexus

When npm installs through a corporate Nexus proxy, the package registry and the standalone archive mirror are separate settings:

```bash
npm config get registry
npm config get selenium-pw-migrator-base-url
```

A healthy Nexus-backed install usually has:

```bash
npm config set registry https://nexus.example/repository/npm-group/
npm config set selenium-pw-migrator-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

Then verify in an isolated temp project without changing global npm state:

```powershell
./scripts/smoke-npm-registry-install.ps1 `
  -Package selenium-pw-migrator@0.0.0-preview.8 `
  -Registry https://nexus.example/repository/npm-group/ `
  -StandaloneBaseUrl https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```
