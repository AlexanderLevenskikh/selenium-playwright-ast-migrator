# selenium-pw-migrator npm wrapper

This package is a thin npm wrapper around the standalone Selenium Playwright Migrator CLI. It does **not** require the .NET SDK or .NET Runtime on the target machine.

During `npm install`, the package downloads the matching standalone release archive for the current OS/architecture and stores the native CLI under `native/<runtime>/` inside the installed package.

## Install

```bash
npm install -g selenium-pw-migrator
selenium-pw-migrator --version
```


Install directly from a GitHub Release asset before the package is published to npm:

```bash
npm install -g https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.5/selenium-pw-migrator-0.0.0-preview.5.tgz
selenium-pw-migrator --version
```

Supported runtimes:

- `win-x64`
- `linux-x64`
- `osx-x64`
- `osx-arm64`

## Configuration

The installer supports these environment variables:

| Variable | Purpose |
|---|---|
| `SELENIUM_PW_MIGRATOR_VERSION` | Override the release version to download. Defaults to the npm package version. |
| `SELENIUM_PW_MIGRATOR_BASE_URL` | Override the GitHub/Nexus/static release asset directory. |
| `SELENIUM_PW_MIGRATOR_RUNTIME` | Override runtime detection, for example `win-x64`. |
| `SELENIUM_PW_MIGRATOR_ARCHIVE_PATH` | Use a local standalone archive instead of downloading. Useful for smoke tests. |
| `SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH` | Verify a local archive using a local `checksums.sha256`. |
| `SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD` | Skip native download during install. |

Example internal mirror install:

```bash
SELENIUM_PW_MIGRATOR_BASE_URL=https://nexus.example/repository/migrator/releases/v0.0.0-preview.5 \
  npm install -g selenium-pw-migrator@0.0.0-preview.5
```
