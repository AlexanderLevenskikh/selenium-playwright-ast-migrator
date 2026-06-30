# Init wizard and project onboarding

`init --wizard` creates a safe starter workspace for a new Selenium → Playwright migration.

The wizard is intentionally conservative:

- it never edits source tests;
- it never overwrites a non-empty migration workspace;
- it writes a reviewable starter config instead of hidden behavior;
- it prefers exact next commands over vague setup instructions;
- it asks before installing lightweight agent loop files.

## Interactive form

```bash
selenium-pw-migrator init --wizard
```

The wizard asks for:

1. source Selenium path;
2. target backend (`playwright-dotnet` or `playwright-typescript`);
3. target test framework for Playwright .NET (`nunit` or `xunit`);
4. whether a target Playwright project already exists;
5. default test id attribute (`data-testid`, `data-test-id`, `data-test`, `data-tid`, or custom);
6. whether to install lightweight agent loop files.

## Non-interactive form

```bash
selenium-pw-migrator init --wizard \
  --source ./OldTests \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration \
  --test-id-attribute data-tid \
  --install-kit
```

Mode-compatible form is also supported:

```bash
selenium-pw-migrator --mode init --wizard \
  --input ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --out migration
```

## Generated workspace

```text
migration/
  profiles/adapter-config.json
  current-ticket.md
  state/run-ledger.md
  README.md
  next-commands.md
  .gitignore
  scaffold/                 # when no existing target project is selected and target is Playwright .NET
  .agent-loops/             # only with --install-kit or interactive confirmation
```

## Existing target project

When the target Playwright project already exists, use:

```bash
selenium-pw-migrator init --wizard \
  --source ./OldTests \
  --target dotnet \
  --target-project ./PlaywrightTests
```

The wizard skips scaffold generation and writes a `discover-target` command into `next-commands.md`.

## First validation step

Always run the generated config through validation before the first migration:

```bash
dotnet run --project Migrator.Cli -- \
  --mode config-validate \
  --config migration/profiles/adapter-config.json \
  --validation-mode strict \
  --out migration/config-validate
```
