# Profile Marketplace

`profile` commands make reusable migration profiles visible and installable without hiding behavior inside the tool.
The first implementation is intentionally offline-only: it uses built-in profiles bundled with the CLI and does not call a remote index.

## Commands

```bash
selenium-pw-migrator profile list
selenium-pw-migrator profile search selenium-nunit
selenium-pw-migrator profile inspect basic-csharp-xunit
selenium-pw-migrator profile install basic-csharp-nunit --out profiles
selenium-pw-migrator profile diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff
```

Mode-compatible forms are also available:

```bash
selenium-pw-migrator --mode profile-list --out profile-marketplace
selenium-pw-migrator --mode profile-search --input xunit --out profile-marketplace
selenium-pw-migrator --mode profile-inspect --input basic-csharp-xunit --out profile-inspect
selenium-pw-migrator --mode profile-install --input basic-csharp-nunit --out profiles
selenium-pw-migrator --mode profile-diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff
```

## Built-in profiles

The bundled starter profiles are deliberately small:

- `basic-csharp-nunit` — Selenium C# / NUnit to Playwright .NET / NUnit starter layer.
- `basic-csharp-xunit` — Selenium C# / xUnit to Playwright .NET / xUnit starter layer.
- `basic-csharp-nunit-data-tid` — NUnit starter layer with `data-tid` as the default test id attribute.

They set safe host and verification defaults, but do not include project-specific selectors, PageObject mappings, source suppressions, or broad source-only identifiers.

## Safety model

Profile install writes a reviewed config layer such as:

```text
profiles/basic-csharp-xunit.adapter-config.json
profiles/basic-csharp-xunit.profile-metadata.json
profiles/profile-install-report.md
```

Existing files are not silently overwritten. If the target config already exists, install writes `<profile-id>.adapter-config.new.json` instead.

Before using a profile in a real run:

```bash
selenium-pw-migrator profile inspect basic-csharp-xunit --out profile-inspect
selenium-pw-migrator profile install basic-csharp-xunit --out profiles
selenium-pw-migrator --mode config-validate --config profiles/basic-csharp-xunit.adapter-config.json --validation-mode production --out config-validate
```

Then layer project-specific mappings after the built-in profile:

```bash
selenium-pw-migrator --mode migrate \
  --input ./OldTests \
  --config profiles/basic-csharp-xunit.adapter-config.json \
  --config migration/profiles/project.adapter-config.json \
  --out migration/generated
```

## Diff workflow

Use `profile diff` before replacing or layering profiles:

```bash
selenium-pw-migrator profile diff \
  --before migration/profiles/adapter-config.json \
  --after basic-csharp-xunit \
  --out profile-diff
```

The diff report highlights count-level changes and risky additions such as method suppressions or source-only identifiers.

## Remote profiles

Remote profile indexes are intentionally not implemented yet. They should only be added after a trust/signing model exists.
