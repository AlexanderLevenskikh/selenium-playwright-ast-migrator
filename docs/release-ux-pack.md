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


## Product-repo onboarding wizard

For a real product repository, users can now start with one command instead of picking the whole route manually:

```bash
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
```

The wizard writes:

- `start-summary.md/json`;
- `migration/profiles/adapter-config.start.json`;
- `migration/README.start.md`;
- `migration/next-commands.md`;
- `migration/current-ticket.md`;
- `migration/state/start-dispatch.json`.

It prints the next safe chain: `doctor install`, agent bootstrap when needed, `pilot`, `doctor`, manual migrate or `/supervised-task`, optional `discover-target`, and dashboard generation after a run exists. Agent values are `opencode`, `codex`, `generic`, and `manual`. `/supervised-task` must treat the start artifacts as the active ticket instead of asking the user for a broad menu.

## Representative pilot slice

Before the first real batch, users can let the CLI choose a bounded representative slice:

```bash
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

The command writes `pilot-selection.md/json`, `selected-tests.txt`, `next-commands.md`, and a copied `selected-input/` directory. The generated analyze/migrate commands point at `selected-input/`, not the full suite. It scores Selenium-like files and tries to cover simple smoke tests, PageObject-heavy files, table/filter patterns, assertions, waits, custom helpers, XPath selectors, data-driven tests, and base fixtures.

## TODO root causes and suggested config patch

`explain-todo` now writes `suggested-config-patch.md/json` next to `explain-todo.md/json`. The patch is deliberately review-first: it highlights “fix this profile mapping first”, adds confidence/evidence badges, and drafts UiTarget/Method/Table entries without applying them automatically.

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


## SUCCESS checkpoint / explicit continue

SUCCESS checkpoints default to stop-for-review. After FINAL/PASS, `/supervised-task` reports evidence, remaining risks, and one recommended `/supervised-task continue ...` command. It must not silently start another run unless the user explicitly says `continue` or bounded auto-continuation is recorded for that exact next action.
