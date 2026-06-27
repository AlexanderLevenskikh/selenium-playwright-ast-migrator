# Agent playbook: TypeScript Playwright target

Use this playbook for Playwright TypeScript output. Generation can run without a TS project; project-aware verification requires an existing Playwright TypeScript project.

## Rules

- Use `--target ts` to generate `.spec.ts` files; reserve `--ts-project` for project-aware verification.
- Do not edit generated `.spec.ts` manually.
- Prefer TS-specific adapter/profile overrides.
- Do not assume C# helper names exist in TS.
- If TS verification reports missing identifiers/imports, classify them before editing config.

## Loop

1. Run `doctor` for the Selenium source and config/profile layers.
2. Run `migrate --target ts` to generate `.spec.ts` files.
3. Run `verify-ts-project` against the migration output.
4. Read `ts-project-verify-report.md` and `agent-ts-verify-next-task.md`.
5. Fix only TS profile/config mappings.
6. Run `config-validate`, `config-diff`, and `guard` where applicable.
7. Stop and ask `Продолжить?`.

## Escalate to developer

Escalate if:

- TS renderer emits invalid syntax for a generic action.
- TS project conventions require new target-specific config schema.
- A C# helper has no TS equivalent and business semantics are unclear.
