# Expected init wizard workspace

Running this non-interactive command:

```bash
selenium-pw-migrator init --wizard \
  --source examples/public-demo/selenium-csharp-xunit \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration/public-demo-xunit \
  --test-id-attribute data-testid \
  --install-kit
```

creates a starter workspace like this:

```text
migration/public-demo-xunit/
  .agent-loops/
  .gitignore
  README.md
  current-ticket.md
  next-commands.md
  profiles/
    adapter-config.json
  scaffold/
    Migration.Playwright.Tests.csproj
    LoginSmokeFactsPlaywright.generated.cs
  state/
    run-ledger.md
```

The generated config should pass `config-validate`, and the scaffold should use xUnit package defaults.
