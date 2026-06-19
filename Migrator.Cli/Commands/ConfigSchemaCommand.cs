using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.SeleniumCSharp;

internal static class ConfigSchemaCommand
{
public static int RunConfigSchema(string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    var schemaText = LoadAdapterConfigSchemaText();
    var schemaPath = Path.Combine(outPath, "adapter-config.schema.json");
    File.WriteAllText(schemaPath, schemaText);

    if (format == "text" || format == "both")
        File.WriteAllText(Path.Combine(outPath, "adapter-config.schema.usage.md"), WriteConfigSchemaUsageMarkdown(schemaPath));
    if (format == "json" || format == "both")
    {
        var report = new ConfigSchemaReport(DateTimeOffset.UtcNow, Path.GetFullPath(schemaPath), "adapter-config.schema.json", new[]
        {
            "Add a $schema property to adapter-config/profile files for editor hints.",
            "Run config-validate after schema edits; JSON Schema complements but does not replace safety validation."
        });
        File.WriteAllText(Path.Combine(outPath, "config-schema-report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    Console.WriteLine($"Adapter-config JSON Schema written to: {Path.GetFullPath(schemaPath)}");
    return 0;
}

public static string LoadAdapterConfigSchemaText()
{
    foreach (var candidate in AdapterConfigSchemaCandidatePaths())
    {
        if (File.Exists(candidate))
            return File.ReadAllText(candidate);
    }

    return MinimalAdapterConfigSchemaText();
}

public static IEnumerable<string> AdapterConfigSchemaCandidatePaths()
{
    yield return Path.Combine(Environment.CurrentDirectory, "schemas", "adapter-config.schema.json");
    yield return Path.Combine(AppContext.BaseDirectory, "schemas", "adapter-config.schema.json");

    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null)
    {
        yield return Path.Combine(dir.FullName, "schemas", "adapter-config.schema.json");
        dir = dir.Parent;
    }
}

public static string WriteConfigSchemaUsageMarkdown(string schemaPath)
{
    return $$"""
# Adapter Config JSON Schema

Schema written to:

```text
{{Path.GetFullPath(schemaPath)}}
```

Use it in config/profile files:

```json
{
  "$schema": "./schemas/adapter-config.schema.json"
}
```

For profile layers, use a relative path from the profile file to the schema file.

Important:

- JSON Schema helps editors and agents with field names, autocomplete, and obvious type errors.
- It does **not** replace `config-validate`.
- After agent config changes, always run:

```powershell
selenium-pw-migrator --mode config-validate --config adapter-config.json --out config-validate
```
""";
}

public static string MinimalAdapterConfigSchemaText()
{
    return """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.local/selenium-playwright-ast-migrator/adapter-config.schema.json",
  "title": "Selenium Playwright AST Migrator adapter-config",
  "type": "object",
  "additionalProperties": true,
  "properties": {
    "$schema": { "type": "string" },
    "SourceProjectName": { "type": "string" },
    "SourceOnlyIdentifiers": { "type": "array", "items": { "type": "string" } },
    "TargetKnownTypes": { "type": "array", "items": { "type": "string" } },
    "TargetKnownIdentifiers": { "type": "array", "items": { "type": "string" } },
    "UiTargets": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "Methods": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "ParameterizedMethods": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "PageObjects": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "Tables": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "Pagination": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "Scopes": { "type": "array", "items": { "type": "object", "additionalProperties": true } },
    "Verification": { "type": "object", "additionalProperties": true },
    "QualityGates": { "type": "object", "additionalProperties": true }
  }
}
""";
}
}
