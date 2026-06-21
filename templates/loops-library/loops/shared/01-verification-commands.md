# Verification Commands

Discover actual commands from project files:

- `package.json`;
- `.csproj` / `.sln`;
- CI config;
- README/docs;
- Makefile/task runner;
- existing scripts.

Prefer project scripts over invented commands.

## Frontend examples

```bash
yarn install --frozen-lockfile
yarn build
yarn typecheck
yarn lint
yarn test
npx playwright test
```

or:

```bash
npm ci
npm run build
npm run typecheck
npm run lint
npm test
```

## .NET examples

```bash
dotnet restore
dotnet build
dotnet test
```

Prefer explicit project paths when known:

```bash
dotnet test Path.To.Tests/Path.To.Tests.csproj
```

## Verification strategy

For small/local changes:

- run targeted tests first;
- then run broader checks before final acceptance.

For risky changes:

- build;
- typecheck/compile;
- tests;
- lint;
- runtime smoke when applicable.

## IDE runner warning

If an IDE runner reports a confusing failure but CLI command succeeds, trust CLI output.

Example:

```text
dotnet.exe exited with code '0': Not available
```

This likely indicates IDE runner trouble, not necessarily test failure.
