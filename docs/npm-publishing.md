# Publish npm wrapper

After the first token-based publish succeeds, switch to [npm Trusted Publishing](npm-trusted-publishing.md) to avoid long-lived npm tokens. Use [final release checklist](release-final-checklist.md) before running a real product-project pilot.

The npm package is a thin Node.js wrapper over the standalone GitHub Release archives. Publish it only after the same version was released on GitHub and the release contains:

- `selenium-pw-migrator-<version>.tgz`
- all standalone runtime archives
- `checksums.sha256`
- `standalone-release-manifest.json`

Preview versions must be published with the `preview` dist-tag. This preview dist-tag keeps prereleases away from the default stable install path. Reserve `latest` for the first stable release. The workflow and publish scripts reject prerelease versions when `publish_tag=latest`, so a preview cannot accidentally become the default npm install.

## First-time npm setup

For the first publish, use a token-based publish flow. Trusted Publishing is easier to enable after the package already exists.

1. Create an npm granular access token with package creation/publish rights.
   - Recommended name: `github-actions-selenium-pw-migrator`
   - Permission: read/write
   - Package scope: all packages, because the package may not exist yet
   - Bypass 2FA for automation when npm offers that option
2. Add the token to GitHub:
   - Repository or environment secret name: `NPM_TOKEN`
   - Environment: `npm-production`
3. Run the **Publish npm Wrapper** workflow with:
   - `dry_run=true`
   - `publish_tag=preview`
   - `use_provenance=false`
4. If the dry run is clean, rerun with:
   - `dry_run=false`
   - `publish_tag=preview`
   - `use_provenance=false`

After the first successful publish, you can configure npm Trusted Publishing for `.github/workflows/publish-npm.yml` and use `use_provenance=true` without relying on `NPM_TOKEN`.

## Manual dry run from GitHub Release asset

```bash
VERSION=0.0.0-preview.8
mkdir -p artifacts/npm
curl -fsSL "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v${VERSION}/selenium-pw-migrator-${VERSION}.tgz" \
  -o "artifacts/npm/selenium-pw-migrator-${VERSION}.tgz"
NPM_DRY_RUN=true NPM_TAG=preview bash scripts/publish-npm-wrapper.sh "$VERSION"
```

Windows PowerShell:

```powershell
$version = "0.0.0-preview.8"
New-Item -ItemType Directory -Force artifacts/npm | Out-Null
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v$version/selenium-pw-migrator-$version.tgz" `
  -OutFile "artifacts/npm/selenium-pw-migrator-$version.tgz"
./scripts/publish-npm-wrapper.ps1 -Version $version -DryRun -Tag preview
```

## GitHub Actions workflow

Use **Publish npm Wrapper** (`.github/workflows/publish-npm.yml`). Keep `dry_run=true` first. When the dry run is clean, rerun with `dry_run=false`.

The workflow downloads the `.tgz` from the matching GitHub Release by default:

```text
https://github.com/<owner>/<repo>/releases/download/v<version>/selenium-pw-migrator-<version>.tgz
```

You can override it with `package_url` for a private mirror or a one-off smoke.

Workflow inputs:

| Input | Default | Purpose |
|---|---:|---|
| `version` | required | Package version, for example `0.0.0-preview.8`. |
| `registry` | `https://registry.npmjs.org/` | npm registry URL. |
| `package_url` | empty | Optional explicit `.tgz` URL. |
| `publish_tag` | `preview` | npm dist-tag. Use `preview` for prereleases and `latest` only for stable releases. |
| `dry_run` | `true` | Run `npm publish --dry-run` only. |
| `use_provenance` | `false` | Add `--provenance` for Trusted Publishing. Keep `false` for token-based first publish. |

## Authentication

The workflow supports two registry authentication modes:

1. `NPM_TOKEN` repository/environment secret, written to npm config before publish.
2. npm Trusted Publishing/provenance, using GitHub OIDC with `id-token: write` and `npm publish --provenance`.

Use the `npm-production` GitHub environment so real publishes require explicit approval.

## Quick global smoke

For a quick human smoke on a clean machine, you can install the preview dist-tag or the pinned preview version globally:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator --version

npm install -g selenium-pw-migrator@0.0.0-preview.8
selenium-pw-migrator --version
```

Use the isolated smoke scripts below for CI and Nexus checks, because they do not pollute global npm state.

## Post-publish smoke

After publishing, prefer the isolated smoke scripts. They create a temporary npm project, install the published package, run the local wrapper binary, and remove the temp directory. This avoids polluting global npm state while still exercising `postinstall`, native payload download, checksum verification, wrapper dispatch, and CLI exit-code propagation.

Public npm registry:

```powershell
./scripts/smoke-npm-registry-install.ps1 `
  -Package selenium-pw-migrator@0.0.0-preview.8
```

```bash
scripts/smoke-npm-registry-install.sh \
  --package selenium-pw-migrator@0.0.0-preview.8
```

Expected metadata should still come from the standalone payload:

```text
distribution: standalone
self-contained: true
```

## Corporate Nexus post-publish smoke

When direct npmjs/GitHub access is blocked, smoke the published package through the corporate npm proxy and point the native payload download at the internal standalone mirror:

```powershell
./scripts/smoke-npm-registry-install.ps1 `
  -Package selenium-pw-migrator@0.0.0-preview.8 `
  -Registry https://nexus.example/repository/npm-group/ `
  -StandaloneBaseUrl https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

```bash
scripts/smoke-npm-registry-install.sh \
  --package selenium-pw-migrator@0.0.0-preview.8 \
  --registry https://nexus.example/repository/npm-group/ \
  --standalone-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

The npm registry proxy and the standalone archive mirror are separate concerns: the first serves the npm package, the second serves the native `.zip` / `.tar.gz` payload used during `postinstall`.
