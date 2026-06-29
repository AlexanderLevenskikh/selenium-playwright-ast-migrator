# Public launch checklist

Run this checklist before publishing a preview package or GitHub release.

## Repository readiness

- [ ] `README.md` and `README.ru.md` describe stable and experimental paths consistently.
- [ ] `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, and `CHANGELOG.md` are present.
- [ ] Issue templates exist for bugs, migration gaps, and profile requests.
- [ ] `docs/public-roadmap.md` is linked from the README and docs index.
- [ ] Demo assets under `examples/public-launch-demo/` are self-contained.
- [ ] Screenshot walkthrough assets under `assets/walkthrough/` render in GitHub.

## Package readiness

- [ ] `dotnet test` passes.
- [ ] `./scripts/pack-tool.sh` or `./scripts/pack-tool.ps1` creates a `.nupkg`.
- [ ] `scripts/verify-nupkg-contents.*` passes for the package.
- [ ] `scripts/smoke-local-tool-package.*` can install and run `selenium-pw-migrator --help` and `--mode doctor`.
- [ ] Package metadata points to `https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator`.
- [ ] The package does not contain `.agent-state`, local migration workspaces, temp folders, or private artifacts.

## Demo readiness

- [ ] The demo starts with `selenium-pw-migrator --mode doctor`.
- [ ] The demo migration writes generated code and reports into a local `migration/` folder.
- [ ] The before/after report explains mapped selectors, remaining TODOs, and next actions.
- [ ] The GitHub Actions example uploads generated artifacts instead of committing generated files automatically.

## Release text

- [ ] GitHub release body is based on `docs/release-notes/v0.6.0-preview.1.md`.
- [ ] NuGet release notes are short and mention public preview scope.
- [ ] Known limitations are explicit: no selector invention, project profiles required for best results, experimental Java/Python/TypeScript paths.
