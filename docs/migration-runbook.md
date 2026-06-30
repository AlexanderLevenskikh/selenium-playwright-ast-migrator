# Migration Runbook

`runbook` is the planning mode for production migrations. It reads a Selenium test project or a narrowed test folder and produces a practical migration plan before the first generated-code run.

```bash
selenium-pw-migrator runbook --input ./OldTests --target dotnet --target-test-framework xunit --out runbook --format both
```

Mode-compatible form:

```bash
selenium-pw-migrator --mode runbook --input ./OldTests --config ./adapter-config.json --out runbook --format both
```

## What it writes

- `runbook.md` — human-readable migration plan.
- `runbook.json` — machine-readable plan for agents/automation.

The report contains:

- project summary and source framework detection signals;
- recommended pilot scope;
- first command chain;
- risk map;
- artifacts to collect;
- acceptance checklist;
- recommended next actions.

## Safety model

`runbook` is read-only. It never edits source tests, target projects, or config files. It may recommend commands such as `doctor`, `index-pom`, `helper-inventory`, `migrate`, `verify`, `report serve`, and `evidence pack`, but it does not run them for you.

## Recommended workflow

1. Run `runbook` on the smallest realistic source test folder.
2. Review the pilot candidates and risks.
3. Run the command chain from the report.
4. Collect the listed artifacts.
5. Use the acceptance checklist before scaling beyond the pilot.

## Production guidance

The report explicitly calls out selector evidence work: prove selectors through Selenium POM/source truth before adding active mappings.

Use the runbook to avoid starting with the entire suite. A good first pilot is usually compact, has direct Selenium locator/action/assertion signals, and avoids heavy dynamic selectors, custom helper stacks, frames/dialogs, and business-critical flaky paths. Run `index-pom` and `helper-inventory` before converting project-specific PageObjects or helper methods into config mappings.
