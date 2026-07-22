# OpenCode migration team

The installed OpenCode pack exposes one user command:

```text
/supervised-task
```

It resolves the configured Selenium source, runs an optional representative pilot, executes one full standard migration run, performs real project verification when possible, and then fixes at most one highest-payoff root cause per continuation cycle.

There is no partition planner, partition manager, partition acceptance state, or partition-advance command.

## Roles

- `orchestrator` — owns the linear run and chooses the next bounded action.
- `executor` — implements one bounded workspace-safe adapter-config/generated-helper/generated-POM change; engine defects are reported with a minimal reproduction unless Migrator repository edits were explicitly authorized.
- `reviewer` — checks semantic preservation and regressions.
- `watchdog` — investigates loops, crashes, and no-progress reruns.

## Safety

All generated and proposed artifacts remain under `migration/**` until reviewed. Missing SDK/project context or a CLI crash is reported as a blocker; agents must never manufacture validation evidence.
