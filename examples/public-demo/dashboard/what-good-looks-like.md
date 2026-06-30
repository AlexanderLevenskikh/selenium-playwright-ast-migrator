# What good looks like

Use this checklist when reviewing the demo output or a first real pilot.

## Green signals

- Generated code uses reviewed config/source evidence for locators.
- `unmappedTargets` is low or zero for the selected slice.
- TODOs are grouped by `MIGRATOR:*` code, not scattered mystery comments.
- Unsupported setup/navigation helpers are visible and ticketable.
- The dashboard names the next owner: config/profile, target infra, source truth, test data, or product semantics.

## Yellow signals

- Setup/navigation remains unsupported, but test body actions are mapped.
- Runtime traces show environment/auth failures unrelated to migration logic.
- Generated code compiles after adding target project package defaults.

## Red signals

- Selectors appear in generated code without source evidence.
- Assertions are suppressed without a documented reason.
- Runtime failures cannot be linked back to generated files or migration reports.
- Evidence packs include source files unintentionally.
