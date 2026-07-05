# Init wizard and project onboarding

`start` is the default product-repo onboarding command. It creates a profile skeleton, a current ticket, a no-menu dispatch state for `/supervised-task`, and exact next commands for `doctor install`, `pilot`, agent bootstrap or manual migration, and dashboard review after a run exists.

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

Use `init --wizard` only when you deliberately want the older manual scaffold/config wizard without the product `start` state. It remains supported for no-agent experiments, local scaffold generation, and tests that need the classic workspace layout.

## When to use which command

| Need | Preferred command |
|---|---|
| Product repo onboarding | `start` |
| Representative first slice | `pilot` |
| OpenCode handoff | `kit bootstrap-opencode` |
| Codex/generic/CI handoff | `kit bootstrap-agent` |
| Manual starter config/scaffold only | `init --wizard` |

## `start` generated workspace

```text
migration/
  current-ticket.md
  next-commands.md
  README.start.md
  profiles/adapter-config.start.json
  state/start-dispatch.json
```

`start-dispatch.json` and `current-ticket.md` exist so `/supervised-task` can choose the next bounded action without asking the user broad menu questions.

## `pilot` generated workspace

```text
migration/pilot/
  pilot-selection.md
  pilot-selection.json
  selected-tests.txt
  selected-input/
  next-commands.md
```

The generated pilot next commands must use `selected-input/`, not the full Selenium suite.

## Legacy manual wizard

```shell
selenium-pw-migrator init --wizard
```

The wizard asks for:

1. source Selenium path;
2. target backend (`playwright-dotnet` or `playwright-typescript`);
3. target test framework for Playwright .NET (`nunit` or `xunit`);
4. whether a target Playwright project already exists;
5. default test id attribute (`data-testid`, `data-test-id`, `data-test`, `data-tid`, or custom);
6. whether to install lightweight agent loop files.

## Non-interactive legacy form

```shell
selenium-pw-migrator init --wizard \
  --source ./OldTests \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration \
  --test-id-attribute data-tid \
  --install-kit
```

Mode-compatible form is also supported:

```shell
selenium-pw-migrator --mode init --wizard \
  --input ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --out migration
```

## Legacy generated workspace

```text
migration/
  profiles/adapter-config.json
  current-ticket.md
  state/run-ledger.md
  README.md
  next-commands.md
  .gitignore
  scaffold/                 # when no existing target project is selected and target is Playwright .NET
  opencode-team/            # guarded OpenCode templates, when installed
```

## Existing target project

When the target Playwright project already exists and you are using the manual wizard, pass the target project path:

```shell
selenium-pw-migrator init --wizard \
  --source ./OldTests \
  --target dotnet \
  --target-project ./PlaywrightTests
```

The wizard skips scaffold generation and writes a `discover-target` command into `next-commands.md`.

## First validation step

Always run the generated config through validation before the first migration:

```shell
selenium-pw-migrator --mode config-validate \
  --config migration/profiles/adapter-config.json \
  --validation-mode strict \
  --out migration/config-validate
```
