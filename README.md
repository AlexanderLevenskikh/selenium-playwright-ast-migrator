# Selenium Playwright Migrator MCP — alpha

Local, read-only MCP server for Selenium C# → Playwright .NET. This source tree is staged in the original repository branch `split/mcp-alpha`, intended for a standalone `selenium-playwright-migrator-mcp` repository.

The MCP host depends on the versioned **`SeleniumPlaywrightMigrator.Application` NuGet package**. It does not reference the Core source projects. Until publication, CI builds a local NuGet feed from the Core staging branch.

## Developer setup

For the current staging setup, clone the Core branch into `./core` and pack its version-matched modules. This source checkout is **only needed to make the local feed**, not to compile the MCP host:

```sh
git clone --branch split/core-alpha https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator.git core
mkdir -p artifacts/nuget
for project in Migrator.Core Migrator.Roslyn Migrator.SeleniumCSharp Migrator.PlaywrightDotNet Migrator.Application; do
  dotnet pack "core/$project/$project.csproj" -c Release -o artifacts/nuget
done
dotnet restore Migrator.Mcp/Migrator.Mcp.csproj --source "$PWD/artifacts/nuget" --source https://api.nuget.org/v3/index.json
dotnet build Migrator.Mcp/Migrator.Mcp.csproj --no-restore -c Release
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
- `explain_finding(source, findingId, config?, expectedAnalysisId?)` — source-backed explanation and next action.
- `preview_migration(source, config?, expectedAnalysisId?)` — conservative generated draft and target identity; no build or behavior assertions.

Use the included [agent skill](skills/selenium-playwright-migration/SKILL.md). The initial API explicitly blocks multi-fixture files and xUnit source instead of silently dropping tests. It does not provide `verify_artifact` or safe agent-edit verification yet.

License: MIT, inherited from source project.
