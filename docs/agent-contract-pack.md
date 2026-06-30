# Agent Contract Pack

`agent contract` generates a ticket-specific instruction pack for a safe migration agent loop.

Use it when you already have a migration ticket, runbook, dashboard triage output, selector evidence, runtime feedback, or a migration run directory and want to hand one bounded task to an agent without expanding the source-edit boundary.

```bash
selenium-pw-migrator agent contract \
  --input migration/current-ticket.md \
  --config ./adapter-config.json \
  --out migration/agent-contract \
  --format both
```

Mode form:

```bash
selenium-pw-migrator --mode agent-contract \
  --input migration/runs/latest \
  --config ./adapter-config.json \
  --out migration/agent-contract \
  --format both
```

## Outputs

- `agent-contract.md` — the full human-readable contract.
- `agent-contract.json` — machine-readable contract for agent runners.
- `allowed-paths.md` — read/write and read-only path boundaries.
- `stop-policy.md` — hard stop conditions for the stop policy.
- `next-commands.md` — exact commands the agent should run or report as unavailable.
- `report-template.md` — required final report shape.
- `.agent-loops/coordinator.md` — coordinator role prompt.
- `.agent-loops/migrator.md` — migrator role prompt.
- `.agent-loops/verifier.md` — verifier role prompt.
- `.agent-loops/README.md` — prompt pack index.

## Safety model

The command is read-only with respect to source tests, target product code, and generated Playwright files.

The generated contract tells agents to:

- keep work inside allowed paths;
- prefer config/profile/generator changes over hand-editing generated output;
- never invent selectors;
- refresh selector evidence before selector mapping work;
- stop according to the stop policy on broad suppressions, source-edit needs, or missing validation tools;
- record exact commands and validation results.

## Multi-agent option

The pack can be used by one agent, but it also provides three separated roles:

- **coordinator** — chooses the single next task, owns handoff and scope.
- **migrator** — implements the bounded config/generator/docs/test change.
- **verifier** — validates the change and checks stop policy compliance.

## Recommended flow

```bash
selenium-pw-migrator runbook --input ./OldTests --out migration/runbook --format both
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/report-dashboard --format both
selenium-pw-migrator selector evidence --input migration/runs/latest --config ./adapter-config.json --out migration/selector-evidence --format both
selenium-pw-migrator agent contract --input migration/current-ticket.md --config ./adapter-config.json --out migration/agent-contract --format both
```

Give the agent only the generated `agent-contract.md`, the `.agent-loops` role prompt it needs, and the referenced evidence artifacts.
