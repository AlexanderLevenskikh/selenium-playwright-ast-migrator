# Refactor summary

This iteration focuses on making the repository look less like an experimental script and more like a maintainable migration toolkit.

## Code changes

- Moved self-contained CLI features from `Program.cs` into command classes:
  - `Migrator.Cli/Commands/ProfileMatchCommand.cs`
  - `Migrator.Cli/Commands/RuntimeFailureClassifierCommand.cs`
  - `Migrator.Cli/Commands/ConfigSchemaCommand.cs`
- Moved CLI/report DTOs to `Migrator.Cli/Models/CliReportModels.cs`.
- Kept `Program.cs` as the compatibility entrypoint/router.
- Added architecture guidance: new CLI modes should be implemented as command classes, not appended to `Program.cs`.

## Documentation changes

- Rewrote the English `README.md` as a product-style landing page.
- Added `docs/README.md` documentation index.
- Added `docs/agent-modes.md` for Strict/Creative agent workflows.
- Added `docs/refactoring-notes.md` for follow-up refactoring plan.
- Updated `GUIDE.md` with TypeScript scenario and agent modes.
- Reworked `examples/agent-first/start-strict.md` and `examples/agent-first/start-creative.md` to include project paths and mode-specific rules.
- Updated agent playbooks index with mode selection guidance.

## Intentional non-changes

- Did not split `PlaywrightDotNetRenderer` yet: this should be a dedicated behavior-preserving refactor with focused tests.
- Did not split the largest test files yet: safer after the current migrator behavior stabilizes.
- Did not delete pilot docs: they may still be useful as historical examples; docs index now points to the current recommended docs first.
