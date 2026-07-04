# selenium-pw-migrator npm wrapper

This package is a thin npm wrapper around the standalone Selenium Playwright Migrator CLI. It does **not** require the .NET SDK or .NET Runtime on the target machine.

During `npm install`, the package downloads the matching standalone release archive for the current OS/architecture and stores the native CLI under `native/<runtime>/` inside the installed package.

## Install

Preview releases are published under the `preview` dist-tag. Stable releases will use the default `latest` tag later.

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator --version
```


Install a pinned version from npm or directly from a GitHub Release asset:

```bash
npm install -g selenium-pw-migrator@0.0.0-preview.8
npm install -g https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8.tgz
selenium-pw-migrator --version
```

Supported runtimes:

- `win-x64`
- `linux-x64`
- `osx-x64`
- `osx-arm64`

## Configuration

The installer supports environment variables and equivalent npm config keys. Environment variables take priority.

| Environment variable | npm config key | Purpose |
|---|---|---|
| `SELENIUM_PW_MIGRATOR_VERSION` | `selenium-pw-migrator-version` | Override the release version to download. Defaults to the npm package version. |
| `SELENIUM_PW_MIGRATOR_BASE_URL` | `selenium-pw-migrator-base-url` | Override the GitHub/Nexus/static release asset directory. |
| `SELENIUM_PW_MIGRATOR_RUNTIME` | `selenium-pw-migrator-runtime` | Override runtime detection, for example `win-x64`. |
| `SELENIUM_PW_MIGRATOR_ARCHIVE_PATH` | `selenium-pw-migrator-archive-path` | Use a local standalone archive instead of downloading. Useful for smoke tests. |
| `SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH` | `selenium-pw-migrator-checksums-path` | Verify a local archive using a local `checksums.sha256`. |
| `SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD` | `selenium-pw-migrator-skip-download` | Skip native download during install. |

Example corporate Nexus install, with the npm package served by an npm proxy/group and the native standalone archives served by an internal raw/static mirror:

```bash
npm install -g selenium-pw-migrator@0.0.0-preview.8 \
  --registry=https://nexus.example/repository/npm-group/ \
  --selenium-pw-migrator-base-url=https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

For repeated installs:

```bash
npm config set registry https://nexus.example/repository/npm-group/
npm config set selenium-pw-migrator-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
npm install -g selenium-pw-migrator@0.0.0-preview.8
```
