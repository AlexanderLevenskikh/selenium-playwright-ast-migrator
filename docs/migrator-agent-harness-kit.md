# Standard Migration Agent Kit

The retained kit name is for package compatibility. Its behavior is a thin safety layer around the ordinary Migrator CLI, with no separate migration scheduler or lifecycle state machine.

## Install

```bash
selenium-pw-migrator kit bootstrap-opencode \
  --workspace migration \
  --source ./LegacyTests \
  --opencode-install auto
```

## Run

```bash
selenium-pw-migrator run \
  --input ./LegacyTests \
  --config migration/profiles/adapter-config.json \
  --out migration/runs/run-001 \
  --format both

selenium-pw-migrator verify-project \
  --input ./LegacyTests \
  --config migration/profiles/adapter-config.json \
  --out migration/runs/run-001/verify-project \
  --format both
```

Then run the installed scope, artifact, and final-gate checks. The optional `/supervised-task` command performs the same sequence and may apply at most one bounded high-payoff repair before rerunning the complete source scope.

## What remains

- project-local adapter config and memory;
- source-scope contract;
- real project verification;
- artifact hygiene and final gate;
- four roles: orchestrator, executor, reviewer, watchdog.

No command may manufacture validation evidence or treat a pilot as final project coverage.
