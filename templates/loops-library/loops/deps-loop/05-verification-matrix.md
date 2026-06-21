# Verification Matrix

Discover actual commands from `package.json`, docs, and CI config.
Prefer project scripts over invented commands.

## Common commands

### Install

```bash
yarn install --frozen-lockfile
npm ci
pnpm install --frozen-lockfile
```

For Yarn v1, prefer:

```bash
yarn install --frozen-lockfile
```

### Build

```bash
yarn build
npm run build
pnpm build
```

### Typecheck

```bash
yarn typecheck
npm run typecheck
pnpm typecheck
```

### Lint

```bash
yarn lint
npm run lint
pnpm lint
```

### Tests

```bash
yarn test
npm test
pnpm test
```

### Audit

Use the existing project audit approach.

Examples:

```bash
yarn audit
npm audit
pnpm audit
yarn audit-ci
npm run audit
```

## Checks by risk

### Low-risk batch

- install
- build or typecheck
- relevant tests if available
- audit/dependency tree check

### Medium-risk batch

- install
- build
- typecheck
- tests
- lint when available
- audit/dependency tree check

### High-risk batch

- install
- build
- typecheck
- tests
- lint
- targeted smoke checks when available
- audit/dependency tree check
- migration notes summarized

## Dependency tree checks

Use when useful:

```bash
yarn why <package>
npm ls <package>
pnpm why <package>
```

Confirm vulnerable versions are gone or explain why they remain.


## Runtime smoke

For medium/high-risk batches that may affect runtime behavior, also follow `10-runtime-smoke.md`.

Runtime smoke is especially important when updating runtime frameworks, bundlers, app bootstrap, auth/session packages, UI kits, routing, state management, or environment/config loaders.
