# Publish npm wrapper

The npm package is a thin Node.js wrapper over the standalone GitHub Release archives. Publish it only after the same version was released on GitHub and the release contains:

- `selenium-pw-migrator-<version>.tgz`
- all standalone runtime archives
- `checksums.sha256`
- `standalone-release-manifest.json`

## Manual dry run from GitHub Release asset

```bash
VERSION=0.0.0-preview.6
curl -fsSL "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v${VERSION}/selenium-pw-migrator-${VERSION}.tgz"   -o "artifacts/npm/selenium-pw-migrator-${VERSION}.tgz"
NPM_DRY_RUN=true scripts/publish-npm-wrapper.sh "$VERSION"
```

Windows PowerShell:

```powershell
$version = "0.0.0-preview.6"
New-Item -ItemType Directory -Force artifacts/npm | Out-Null
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version/selenium-pw-migrator-$version.tgz" `
  -OutFile "artifacts/npm/selenium-pw-migrator-$version.tgz"
./scripts/publish-npm-wrapper.ps1 -Version $version -DryRun
```

## GitHub Actions workflow

Use **Publish npm Wrapper** (`.github/workflows/publish-npm.yml`). Keep `dry_run=true` first. When the dry run is clean, rerun with `dry_run=false`.

The workflow downloads the `.tgz` from the matching GitHub Release by default:

```text
https://github.com/<owner>/<repo>/releases/download/v<version>/selenium-pw-migrator-<version>.tgz
```

You can override it with `package_url` for a private mirror or a one-off smoke.

## Authentication

The workflow supports two registry authentication modes:

1. `NPM_TOKEN` repository/environment secret, written to npm config before publish.
2. npm trusted publishing/provenance, using GitHub OIDC with `id-token: write` and `npm publish --provenance`.

Use the `npm-production` GitHub environment so real publishes require explicit approval.

## Post-publish smoke

After publishing, test from a clean shell:

```bash
npm uninstall -g selenium-pw-migrator || true
npm install -g selenium-pw-migrator@0.0.0-preview.6
selenium-pw-migrator --version
```

Expected metadata should still come from the standalone payload:

```text
distribution: standalone
self-contained: true
```
