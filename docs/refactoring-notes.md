# Refactoring notes

This project intentionally keeps the migration pipeline conservative, but the codebase should not look like a one-off script.

## Current decomposition

Large CLI features are being moved out of `Program.cs` into command classes:

| Area | File |
|---|---|
| Runtime log classification | `Migrator.Cli/Commands/RuntimeFailureClassifierCommand.cs` |
| Config schema export | `Migrator.Cli/Commands/ConfigSchemaCommand.cs` |
| Profile reuse scoring | `Migrator.Cli/Commands/ProfileMatchCommand.cs` |
| CLI report DTOs | `Migrator.Cli/Models/CliReportModels.cs` |

`Program.cs` should act as the entrypoint/router. New CLI modes should not add another large region to `Program.cs`; prefer a dedicated command class with one public `Run...` method and private helpers.

## Suggested next refactors

These are intentionally left as follow-up tasks because they should be done with tests after the current migration stabilizes:

1. Move `verify-project` helpers into `ProjectVerifyCommand`.
2. Move `explain-todo`, `smoke-plan`, and `migration-board` into report command classes.
3. Split `PlaywrightDotNetRenderer` into:
   - class/test wrapper rendering;
   - action rendering;
   - safety/TODO rendering;
   - placeholder substitution;
   - locator rendering.
4. Split large test files by behavior area:
   - snapshots;
   - parser recognizers;
   - renderer safety;
   - placeholders;
   - TypeScript target;
   - orchestration.

## Guardrail

Refactoring must be behavior-preserving. Do not combine big refactors with recognizer/renderer behavior changes unless there is a failing regression test proving the need.
