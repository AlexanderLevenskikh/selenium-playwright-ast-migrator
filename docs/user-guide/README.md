# User guide

This guide is for users running migrations, reviewing generated output, and deciding what to improve next.

## Recommended reading order

1. [Quick start](../quick-start.md)
2. [End-to-end simple example](../examples/end-to-end-simple.md)
3. [Migration workflow](migration-workflow.md)
4. [Reports and quality gates](reports-and-quality-gates.md)
5. [Config and profile guide](../config-profile-guide.md)
6. [Limitations](limitations.md)
7. [Troubleshooting](../troubleshooting.md)

## Common workflows

- Product repo onboarding: run `start`, then `pilot`, then execute `migration/pilot/next-commands.md` against `selected-input/`.
- Existing Playwright .NET project: run `discover-target`, review the draft config, then run `orchestrate` on the selected pilot.
- No Playwright infrastructure yet: use `start` and `pilot` first; use `init --wizard` only when you need the legacy `scaffold/` generator.
- Large suite migration: use `orchestrate`, `explain-todo`, `migration-board`, `index-pom`, and `helper-inventory` to reduce repeated root causes before expanding beyond the pilot.
- TypeScript preview: run `migrate --target ts`, then `verify-ts-project --ts-project <existing-playwright-ts-project>`.
