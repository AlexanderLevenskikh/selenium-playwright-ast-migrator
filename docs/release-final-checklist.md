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

## 5. Public preview narrative smoke

Before public demo or release notes, verify the user-facing story is coherent:

```text
public-preview-flow/v1
install -> doctor install -> playground/start -> pilot/wave -> gates -> current-ticket -> mapping research memory -> feedback-bundle/v1
```

Checklist:

- `README.md` and `README.ru.md` link to `docs/public-preview-flow.md` / `.ru.md`.
- `docs/public-preview-flow.md` mentions `feedback-bundle/v1`, `mapping-research-memory/v1`, `verify-project-harness/v1`, `artifact-hygiene/v1`, and `BLOCKED_BY_WAVE_QUALITY_BUDGET`.
- `docs/wave-mode-operator-runbook.md` remains the operational reference for blocked gates and follow-up loops.
- Release notes describe the preview as measurable and reviewable, not as guaranteed automatic conversion.
- The feedback-bundle user path is visible from the root README before users are asked to share artifacts.

## 6. Product-project pilot

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
