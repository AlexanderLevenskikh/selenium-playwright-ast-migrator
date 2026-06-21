# Framework Migration Playbook

Use when dependency update requires code/config migration.

## General rules

- Keep migration scoped to the dependency batch.
- Follow existing project conventions.
- Prefer mechanical migration over refactoring.
- Do not mix framework migration with unrelated audit cleanup.

## Common areas

### React / React DOM

Check root rendering API, StrictMode effects, testing utilities, type packages, peer dependencies.

### React Router

Check route definitions, navigation APIs, hooks, blockers/navigation guards, tests.

### Redux / Redux Toolkit / React Redux

Check store setup, middleware, typed hooks, selectors, connected components.

### TypeScript

Check compiler options, lib target, stricter type errors, generated types, build tooling compatibility.

### ESLint / TypeScript ESLint

Check config format, parser options, plugin compatibility, rule renames/removals.

### Jest / Vitest / Testing Library

Check environment config, fake timers, setup files, matcher imports, async utilities.

### Storybook

Check main config, preview config, builder, addons, stories format.

### Webpack / Vite / Babel

Check config schema, plugin versions, loader compatibility, dev server changes, env vars.

### Playwright

Check config schema, fixtures, reporter config, browser install, CI image compatibility, retries/workers.

## Acceptance

A framework migration batch is acceptable only when build/typecheck/tests pass and migration is limited to selected group.
