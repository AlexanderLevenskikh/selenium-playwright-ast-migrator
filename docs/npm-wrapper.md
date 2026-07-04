# npm wrapper

The npm wrapper is an optional distribution channel for frontend-heavy teams. It lets users install the Selenium Playwright Migrator with `npm install -g selenium-pw-migrator@preview` while the project is in preview. It still runs the same standalone self-contained CLI bundle used by GitHub Release assets.

The package does not embed the .NET SDK or require a local .NET installation. During `postinstall`, it downloads the matching standalone archive for the current platform and installs the native binary inside the npm package directory.

## User install

Preview releases are published under the `preview` dist-tag. Stable releases will use the default `latest` tag later.

```bash
npm install -g selenium-pw-migrator@preview
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


## Install from a GitHub Release asset

The release workflow also attaches the packed wrapper tarball to the GitHub Release. This gives users a pinned fallback when npm registry access is proxied or when a specific preview must be tested directly from release assets:

```bash
npm install -g selenium-pw-migrator@0.0.0-preview.8
npm install -g https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8.tgz
selenium-pw-migrator --version
```

The `.tgz` package still downloads the matching standalone archive during `postinstall`, so the GitHub Release must contain the npm tarball, the standalone archives, and `checksums.sha256` together.

## Internal Nexus/static mirror

The wrapper supports the same flat release asset layout as the standalone installers. This is the recommended corporate setup when npm traffic must go through a Nexus npm proxy and GitHub release downloads are blocked:

```text
<standalone-base-url>/
  selenium-pw-migrator-<version>-win-x64.zip
  selenium-pw-migrator-<version>-linux-x64.tar.gz
  selenium-pw-migrator-<version>-osx-x64.tar.gz
  selenium-pw-migrator-<version>-osx-arm64.tar.gz
  checksums.sha256
```

Use your Nexus npm group/proxy as the npm registry, and point the wrapper at the internal standalone archive mirror:

```bash
npm install -g selenium-pw-migrator@0.0.0-preview.8 \
  --registry=https://nexus.example/repository/npm-group/ \
  --selenium-pw-migrator-base-url=https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

For repeated installs, store both settings in npm config:

```bash
npm config set registry https://nexus.example/repository/npm-group/
npm config set selenium-pw-migrator-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
npm install -g selenium-pw-migrator@0.0.0-preview.8
```

Environment variables still work and take priority over npm config:

```bash
SELENIUM_PW_MIGRATOR_BASE_URL=https://nexus.example/repository/migrator-releases/v0.0.0-preview.8 \
  npm install -g selenium-pw-migrator@0.0.0-preview.8 \
  --registry=https://nexus.example/repository/npm-group/
```

Supported npm config keys mirror the environment variable names:

| npm config key | Environment variable | Purpose |
|---|---|---|
| `selenium-pw-migrator-base-url` | `SELENIUM_PW_MIGRATOR_BASE_URL` | Override the GitHub/Nexus/static release asset directory. |
| `selenium-pw-migrator-version` | `SELENIUM_PW_MIGRATOR_VERSION` | Override the standalone archive version. |
| `selenium-pw-migrator-runtime` | `SELENIUM_PW_MIGRATOR_RUNTIME` | Override runtime detection, for example `win-x64`. |
| `selenium-pw-migrator-archive-path` | `SELENIUM_PW_MIGRATOR_ARCHIVE_PATH` | Use a local standalone archive instead of downloading. |
| `selenium-pw-migrator-checksums-path` | `SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH` | Verify a local archive using a local `checksums.sha256`. |
| `selenium-pw-migrator-skip-download` | `SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD` | Skip native download during install. |

If `postinstall` fails in a corporate network, first check that the standalone mirror URL contains the exact archive name for the npm package version and the selected runtime.

## Local smoke against freshly built standalone archives

After running `scripts/package-standalone.ps1`, run:

```powershell
./scripts/smoke-npm-wrapper.ps1 `
  -Version 0.0.0-preview.8 `
  -Runtime win-x64 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.8-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256
```

The smoke uses `SELENIUM_PW_MIGRATOR_ARCHIVE_PATH` and `SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH`, so it does not need a published GitHub Release.

## Packaging for npm publish

```powershell
./scripts/pack-npm-wrapper.ps1 -Version 0.0.0-preview.8
```

The script stages the wrapper under `artifacts/npm/package`, sets the npm package version, and produces a `.tgz` package with `npm pack`.


## Publishing to npm registry

Publish the wrapper only after the GitHub Release asset install smoke passes. See [npm publishing](npm-publishing.md) for the dry-run-first workflow, `NPM_TOKEN`/Trusted Publishing setup, preview dist-tag policy, and post-publish smoke.
