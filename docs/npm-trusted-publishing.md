# npm Trusted Publishing

The first npm publish is intentionally token-first because it is the most predictable way to create a new public package. After the package exists, switch to npm Trusted Publishing to avoid long-lived `NPM_TOKEN` usage in GitHub Actions.

## Prerequisites

- The npm package `selenium-pw-migrator` already exists.
- The GitHub workflow file is `.github/workflows/publish-npm.yml`.
- The workflow environment is `npm-production`.
- The workflow has `id-token: write` permission.

## Configure npmjs.com

On npmjs.com, open the package settings for `selenium-pw-migrator` and add a Trusted Publisher for GitHub Actions:

```text
Owner: AlexanderLevenskikh
Repository: selenium-playwright-ast-migrator
Workflow filename: publish-npm.yml
Environment: npm-production
```

Keep the workflow filename and environment exact. If either value differs, npm will reject the OIDC exchange.

## Run the workflow

After the package trust relationship is configured, run **Publish npm Wrapper** with:

```text
dry_run: false
publish_tag: preview
use_provenance: true
```

The workflow already supports this mode through `use_provenance`. Token-first mode remains available with `use_provenance=false` and `NPM_TOKEN`.

## Post-publish smoke

```bash
npm view selenium-pw-migrator@preview version
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator --version
```

For corporate Nexus, wait until the npm proxy has synced the new version, then use the isolated registry smoke script:

```bash
scripts/smoke-npm-registry-install.sh \
  --package selenium-pw-migrator@preview \
  --registry https://nexus.example/repository/npm-group/ \
  --standalone-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
```

## Cleanup after switching

After a successful Trusted Publishing release:

- remove or rotate the broad first-publish `NPM_TOKEN`;
- keep `publish_tag=preview` for prereleases;
- use `publish_tag=latest` only for stable versions such as `0.1.0`.
