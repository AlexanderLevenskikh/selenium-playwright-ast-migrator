# Final release and pilot checklist

Use this checklist after npm/standalone packaging changes and before running the migrator on a real product project.

## 1. Repository final gate

Windows PowerShell:

```powershell
./scripts/verify-distribution-final.ps1
```

Linux/macOS/WSL:

```bash
scripts/verify-distribution-final.sh
```

This runs whitespace checks, shell executable-bit checks, npm wrapper syntax checks, bash syntax checks, and `dotnet test Migrator.sln -c Release`.

For the heavier local distribution smoke:

```powershell
./scripts/verify-distribution-final.ps1 -Version 0.0.0-preview.8 -RunPackagingSmoke
```

## 2. GitHub Release smoke

After publishing a preview release, verify the release assets contain:

```text
SeleniumPlaywrightMigrator.<version>.nupkg
selenium-pw-migrator-<version>.tgz
selenium-pw-migrator-<version>-win-x64.zip
selenium-pw-migrator-<version>-linux-x64.tar.gz
selenium-pw-migrator-<version>-osx-x64.tar.gz
selenium-pw-migrator-<version>-osx-arm64.tar.gz
checksums.sha256
standalone-release-manifest.json
install-standalone.ps1
install-standalone.sh
```

Then install through standalone or npm and run:

```bash
selenium-pw-migrator --version
selenium-pw-migrator --help
```

## 3. npm registry or Nexus smoke

Public npm:

```powershell
./scripts/smoke-npm-registry-install.ps1 -Package selenium-pw-migrator@preview
```

Corporate Nexus npm proxy plus standalone mirror:

```powershell
./scripts/smoke-npm-registry-install.ps1 `
  -Package selenium-pw-migrator@0.0.0-preview.8 `
  -Registry https://nexus.example/repository/npm-group/ `
  -StandaloneBaseUrl https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

## 4. Installation diagnostics before project pilot

Run diagnostics from the same shell where the product migration will run:

```powershell
./scripts/diagnose-install.ps1
```

or:

```bash
scripts/diagnose-install.sh
```

Confirm the first resolved command is the intended install channel and `--version` reports the expected version.

## 5. Product-project pilot

Start with a narrow project scope, keep generated files under `migration/**`, and use the guarded OpenCode/Desktop runbook when using agents:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

For manual CLI smoke in a product repository:

```bash
selenium-pw-migrator kit doctor --workspace migration
selenium-pw-migrator kit init --workspace migration --source ./path/to/selenium-tests
selenium-pw-migrator --mode analyze --workspace migration --input ./path/to/selenium-tests --out analysis --format both
```

Do not use product-project results as release evidence until the repository final gate and distribution smoke above pass.
