# Release UX Pack

The release UX pack keeps the public CLI route boring and discoverable: install, diagnose, choose an entry path, open the dashboard, and run a deterministic release gate.

## Install/update without thinking

Recommended public install:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
```

Update:

```bash
npm update -g selenium-pw-migrator
# or print the current channel-specific update command:
selenium-pw-migrator self update
```

`doctor install` writes `install-doctor-report.md/json` and prints:

- resolved executable;
- version and inferred channel (`npm`, `standalone`, `dotnet-tool`, `source`, or `unknown`);
- runtime and framework;
- PATH candidates that may shadow each other;
- recommended install and update commands.

## Three public entry paths

### Try without an agent

```bash
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

### Migrate with OpenCode

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

### Migrate with another agent

```bash
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

`bootstrap-agent` writes `migration/AGENT_HANDOFF.md` so Codex/CI/generic agents no longer need to use `bootstrap-opencode --opencode-install ci` as a workaround.

## Dashboard-first review

After a run, open this first:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Open `migration/dashboard/latest/report-dashboard.html` for readiness, TODO root causes, unsupported actions, generated files, next actions, evidence links, and agent run history.

## Final public release gate

From the repository root:

```bash
selenium-pw-migrator doctor release --out release-doctor --format both
```

The release doctor verifies package metadata, publish workflows, npm/standalone smoke script presence, install diagnostics, `self update`, `bootstrap-agent`, dashboard-first docs, and repository hygiene before heavier CI/publish jobs run.
