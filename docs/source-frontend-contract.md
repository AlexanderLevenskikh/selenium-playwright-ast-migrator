# Source frontend contract

A source frontend owns source-language parsing and lowering into Migrator IR. It should not know target-project helper code, target framework imports, or renderer-specific syntax.

```csharp
public interface ISourceFrontend
{
    SourceSpec Source { get; }
    IReadOnlyCollection<string> Aliases { get; }
    SourceCapabilityReport Capabilities { get; }
    bool CanParse(MigrationRequest request);
    SourceParseResult Parse(MigrationRequest request);
}
```

## Responsibilities

A frontend should:

- identify one source language/framework pair through `SourceSpec`;
- parse source files deterministically;
- preserve source spans/diagnostics where possible;
- lower supported actions into `MigrationDocument` IR V2;
- emit `IrDiagnostic` entries instead of silently dropping unsupported code;
- expose honest capability status.

A frontend should **not**:

- invent selectors that are not present in source/POM/DOM evidence;
- embed target renderer syntax directly unless the syntax is represented as explicit profile data;
- suppress unsupported actions without a diagnostic;
- depend on CLI-only flags inside Core contract implementations.

## Stable identity and aliases

Use `SourceSpec.Id` as the stable machine-readable id in reports and config.

Examples:

| Source id | Language | Framework | Status |
|---|---|---|---|
| `selenium-csharp` | `csharp` | `selenium` | stable |
| `selenium-java` | `java` | `selenium` | experimental MVP |
| `selenium-python` | `python` | `selenium` | experimental spike |

Aliases are only CLI/user input shortcuts. Reports should use the stable id.

## Capability reporting

Every frontend should expose a `SourceCapabilityReport` with schema version `source-capabilities/v1`.

Useful capability areas:

- `semantic-model`;
- `test-frameworks`;
- `selenium-actions`;
- `locators`;
- `waits`;
- `assertions`;
- `page-objects`;
- `target-config`.

Support levels are intentionally human-readable: `strong`, `basic`, `limited`, `none`. Use conservative labels. A frontend without a semantic model should say so.

## Registration

Built-in frontends are registered in `SourceFrontendRegistry`:

```csharp
var registry = new SourceFrontendRegistry()
    .Register(new CSharpSeleniumFrontend(config))
    .Register(new JavaSeleniumFrontend())
    .Register(new PythonSeleniumFrontend());
```

A contributed frontend should include:

- registry resolution tests;
- capability tests;
- fixture parser tests;
- CLI smoke tests for `--source <alias>`;
- docs that explain stable/experimental status.

## Minimum acceptance for a new frontend

- `--mode capabilities` lists the frontend.
- `source-capabilities-report.json/md` is written by source-processing modes.
- Unsupported source constructs produce diagnostics/TODOs, not silent omissions.
- At least one fixture covers actions, locators, waits, assertions, and unsupported fallback.
- The docs state what is production-ready and what is not.
