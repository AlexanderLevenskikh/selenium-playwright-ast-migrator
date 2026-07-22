# Standard migration flow

The supported workflow uses one configured source scope and one ordinary run directory at a time. It does not partition the source or maintain a second execution state machine.

## First run

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator run --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001/verify-project --format both
```

The pilot is optional calibration. It helps expose missing mappings early, but it is not an execution partition and never replaces the complete run.

## Validation

Run the installed checks against the same concrete run:

```shell
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-scope.sh -RepoRoot . -AllowedRoots migration
./migration/scripts/validate-run-artifacts.sh -RunPath migration/runs/run-001
./migration/scripts/check-final-gate.sh -Workspace migration -Run migration/runs/run-001 -RepoRoot .
```

Use the `.ps1` equivalents on Windows. The final gate requires a real passing `verify-project` report by default. A missing SDK, target project, or package source is a blocker, not a reason to create a replacement JSON file.

## Optimized continuation

Optimization is deliberately simple and reviewable:

- read project-scoped migration memory before changing mappings;
- rank repeated TODO/root causes by expected payoff;
- make at most one bounded source-backed adapter-config, generated-helper, or generated-POM improvement inside the product workspace; report a suspected Migrator recognizer/renderer defect with a minimal reproduction unless repository-source edits were explicitly authorized;
- rerun the complete configured source and compare the reports;
- stop after success, a concrete blocker, or repeated no progress.

This removes coordination overhead while preserving the useful safeguards: optional pilot calibration, project memory, reviewable config deltas, scope checks, artifact hygiene, project verification, and an honest final gate.

## Upgrading an old workspace

Archive old run/state artifacts and bootstrap the workspace again. Do not reconstruct old partition state or copy validation evidence into the new run.
