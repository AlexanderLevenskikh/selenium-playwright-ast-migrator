# Doctor fix mode

`doctor --fix` turns selected setup findings into a reviewable repair plan. It is deliberately conservative: it can create migration-workspace files and `.doctor.new` config candidates, but it never edits Selenium source tests and never invents selectors or POM mappings.

## Commands

Dry-run is the default fix behavior:

```bash
selenium-pw-migrator --mode doctor --input ./OldTests --fix --dry-run --out doctor-fix
```

Apply safe fixes explicitly:

```bash
selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --fix --apply --out doctor-fix
```

## Safety model

- `--fix --dry-run` writes only doctor report artifacts under `--out`.
- `--fix --apply` writes only inside the selected workspace/output path, or as `.doctor.new` files next to explicit `--config` paths.
- Existing config files are not overwritten; config repairs are emitted as `<config>.doctor.new` for review.
- Source tests and generated test files are never edited.
- Selector, PageObject, and helper mappings stay recommendation-only until backed by source truth such as `index-pom` or `helper-inventory` evidence.

## Safe automatic fixes

The first implementation can plan/apply:

- missing workspace folders: `profiles/`, `state/`, `runs/`;
- workspace `.gitignore` for generated artifacts;
- starter `profiles/adapter-config.json` when no config exists;
- `.doctor.new` config candidates with:
  - `$schema` hint;
  - `SchemaVersion`;
  - `TestHost.TargetTestFramework`;
  - `Verification` defaults for project-aware compile checks.

## Artifacts

```text
doctor-report.md
doctor-report.json
agent-doctor-next-task.md
doctor-fix-plan.md
doctor-fix-plan.json
doctor-fix.patch
doctor-fix-report.md
```

`doctor-fix.patch` is a review aid. It summarizes planned/created files and `.doctor.new` candidates; it is not meant to be blindly applied with `git apply`.
