# Target backend contract

A target backend owns rendering decisions for one target language/framework. It should be reusable across projects and keep project-specific conventions in profile/config data.

```csharp
public interface ITargetBackend
{
    TargetSpec Target { get; }
    IReadOnlyCollection<string> Aliases { get; }
    TargetCapabilityReport Capabilities { get; }
    string Render(TestFileModel model);
    string RenderDocument(MigrationDocument document);
    string GetDefaultFileName(TestFileModel model);
}
```

## Responsibilities

A backend should:

- identify one target language/framework through `TargetSpec`;
- render legacy `TestFileModel` for compatibility while IR V2 migration continues;
- render `MigrationDocument` for IR V2 paths;
- choose deterministic generated file names;
- keep target syntax generic and profile-driven;
- emit TODO comments for unsupported or unsafe actions instead of guessing.

A backend should **not**:

- hardcode one project's selectors, helper classes, routes, or PageObjects;
- treat source-only identifiers as valid target symbols;
- hide generated TODOs behind dummy code just to make compilation pass;
- claim production status without verification guidance.

## Stable identity and aliases

Use `TargetSpec.Id` as the stable machine-readable id in reports and config.

Examples:

| Target id | Language | Framework | Status |
|---|---|---|---|
| `playwright-dotnet` | `csharp` | `playwright` | stable |
| `playwright-typescript` | `typescript` | `playwright` | experimental preview |

Aliases such as `dotnet`, `cs`, `ts`, or `playwright-csharp` are user convenience only.

## Capability reporting

Every backend should expose a `TargetCapabilityReport` with schema version `target-capabilities/v1`.

Useful capability areas:

- `legacy-ir-rendering`;
- `ir-v2-rendering`;
- `project-verification`;
- `config-driven-mappings`;
- `test-host`;
- `runtime-readiness`.

The report should include limitations and recommended validation commands. Experimental targets should explicitly say which verification path is mandatory.

## Target-specific profile data

When a method mapping has target-specific syntax, prefer:

```json
{
  "SourceMethod": "LoginAsAdmin",
  "Targets": {
    "playwright-typescript": {
      "TargetStatements": ["await loginAsAdmin(page);"]
    },
    "playwright-dotnet": {
      "TargetStatements": ["await LoginAsAdminAsync();"]
    }
  }
}
```

Do not put TypeScript code in legacy C# `TargetStatements` and hope the renderer fixes it. Use `config-validate --target ts --validation-mode production` to catch unsafe mappings.

## Minimum acceptance for a new backend

- `--mode capabilities` lists the backend.
- `target-capabilities-report.json/md` is written by source-processing modes.
- registry tests cover id and aliases;
- renderer tests cover file naming and representative actions/assertions;
- verification docs explain how users should compile/type-check generated output;
- limitations are visible in the capability report.
