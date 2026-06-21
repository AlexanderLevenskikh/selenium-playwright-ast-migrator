# Batch Planning Policy

The agent must split dependency work into safe batches.

## Golden rule

One batch = one clear hypothesis.

Examples:

```text
Update `@playwright/test` family to reduce test-tooling vulnerabilities.
Update `webpack-dev-server` parent to remove vulnerable transitive `ip`.
Add targeted Yarn resolution for vulnerable transitive package after confirming compatibility.
```

## Batch size

Prefer:

- 1 package for major upgrades;
- 1 package family for tightly coupled packages;
- 2–5 packages for low-risk dev tooling;
- lockfile-only or targeted transitive update when possible.

Avoid:

- updating all outdated packages at once;
- mixing runtime framework updates with lint/test/build tooling;
- mixing React/router/state-management migrations with audit cleanup;
- changing Node version, package manager, and dependencies in one batch.

## Package families that may be grouped

- `react`, `react-dom`, related types if needed;
- `typescript`, `ts-node`, `ts-jest` when compatible;
- `eslint`, `@typescript-eslint/*`, eslint plugins/configs;
- `jest`, `ts-jest`, `@types/jest`;
- `vitest`, `@vitest/*`;
- `webpack`, `webpack-cli`, loaders/plugins;
- `vite`, `@vitejs/*`;
- `storybook`, `@storybook/*`;
- `playwright`, `@playwright/test`;
- `babel`, `@babel/*`;
- `redux`, `@reduxjs/toolkit`, `react-redux` only when migration impact is understood.

## Risk classification

### Low risk

- patch/minor update;
- dev dependency;
- type package;
- direct security patch;
- no code migration expected.

### Medium risk

- tooling minor/major update;
- package with config changes;
- package used by many files;
- peer dependency changes.

### High risk

- runtime framework major update;
- router major update;
- state-management major update;
- UI library major update;
- bundler major update;
- TypeScript major update;
- package requiring code migration.

## Acceptance criteria for a batch

Keep the batch only if:

- install succeeds;
- lockfile is consistent;
- build/typecheck/tests pass as applicable;
- audit or dependency target improves;
- lockfile churn is explainable;
- code migration is minimal and scoped.
