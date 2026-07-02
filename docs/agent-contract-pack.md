# Agent Contract Pack

`agent contract` generates a ticket-specific instruction pack for a safe migration agent loop.

Use it when you already have a migration ticket, runbook, dashboard triage output, selector evidence, runtime feedback, or a migration run directory and want to hand one bounded task to an agent without expanding the source-edit boundary.

The default contract is artifact-only. It is designed for agents that use an installed `selenium-pw-migrator` dotnet tool and do not have, need, or search for migrator source code.

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
- write artifact changes under `migration/**` and forbidden real-project changes only as proposals under `migration/proposals/**`;
- prefer config/profile/generator changes over hand-editing generated output;
- never invent selectors;
- refresh selector evidence before selector mapping work;
- stop according to the stop policy on forbidden writes, broad suppressions, source-edit needs, or missing validation tools;
- reject TODO reduction caused by suppression, empty tests, weakened assertions, dummy target-known identifiers, or real target/POM project edits;
- treat `0 TODO` as success only when scope guard, quality gates, and verification evidence pass;
- record exact commands and validation results.

## Invalid success

These outcomes must be rejected even if TODO count is lower:

- TODO removed by suppressing FluentAssertions, NUnit assertions, assertion-like helpers, or business checks.
- TODO removed by emptying generated tests or deleting meaningful actions/assertions.
- TODO removed by adding dummy `TargetKnownIdentifiers` or similar symbol-hiding config.
- TODO removed by editing real target project/POM/project files instead of migration artifacts.
- `FINAL` claimed without scope guard, config-validate/quality gate, and verify/project-verify evidence.

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
