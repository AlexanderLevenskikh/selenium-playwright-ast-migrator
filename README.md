# Selenium Playwright Migrator MCP — alpha

Local, read-only MCP server for Selenium C# → Playwright .NET. This source tree is staged in the original repository branch `split/mcp-alpha`, intended for a standalone `selenium-playwright-migrator-mcp` repository.

It calls the **separate** Core tree via a project reference. The versioned Core NuGet package is not yet available: do not claim the MCP host is independently distributable until a Core package release exists.

## Developer setup

Checkout the future Core repo (or the staging `split/core-alpha` branch) into `./core`, next to `Migrator.Mcp`:

```sh
git clone --branch split/core-alpha https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator.git core
dotnet restore Migrator.Mcp/Migrator.Mcp.csproj
dotnet build Migrator.Mcp/Migrator.Mcp.csproj -c Release
MIGRATOR_WORKSPACE=/absolute/path/to/product dotnet run --project Migrator.Mcp/Migrator.Mcp.csproj -c Release --no-build
```

`MIGRATOR_WORKSPACE` must be an absolute existing directory. Tools accept paths relative to it and do not apply any patch. The server writes protocol messages to stdout, not diagnostics.

## Client example

```json
{
  "mcpServers": {
    "selenium-playwright-migrator": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/mcp/Migrator.Mcp/Migrator.Mcp.csproj", "--configuration", "Release", "--no-build"],
      "env": { "MIGRATOR_WORKSPACE": "/absolute/path/to/product" }
    }
  }
}
```

## Tools

- `analyze_migration(source, config?)` — inventory, source/config hashes, findings, completeness.
- `preview_migration(source, config?, expectedAnalysisId?)` — conservative generated draft and target identity; no build or behavior assertions.

Use the included [agent skill](skills/selenium-playwright-migration/SKILL.md). The initial API explicitly blocks multi-fixture files and xUnit source instead of silently dropping tests. It does not provide `verify_artifact`, `explain_finding` or safe agent-edit verification yet.

License: MIT, inherited from source project.
