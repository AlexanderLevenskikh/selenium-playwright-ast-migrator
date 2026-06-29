# Public Release TODO

Temporary planning file. Remove this document after the public-release work is implemented or moved into real issues.

## 1. Public Release Hygiene

Goal: prepare the package for safe public publication.

Tasks:
- [x] Remove `.agent-state` from NuGet/dotnet-tool package contents.
- [x] Add public package metadata: license, repository URL, project URL, icon, release notes.
- [x] Replace `Company=Internal` and package-facing internal wording.
- [x] Verify that local artifacts, agent state, temp files, and private docs are not packed.
- [x] Add or finalize `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, and `CHANGELOG.md`.

Acceptance criteria:
- `.nupkg` contains only public-safe files.
- `dotnet tool install` works from the local `.nupkg`.
- Package metadata is ready for NuGet/GitHub release.

## 2. CLI Productization

Goal: make the CLI easier to understand, test, and extend.

Tasks:
- [ ] Split the large `Program.cs` mode handling into dedicated command classes. Initial command classes already exist; remaining large legacy blocks should move incrementally.
- [x] Introduce a single command registry/catalog.
- [x] Standardize command options, help output, and exit codes through catalog-backed global/command help.
- [x] Add tests for `--help`/key command help guardrails.
- [x] Separate commands into stable, experimental, and internal groups.

Acceptance criteria:
- [ ] A new CLI command can be added without editing a large monolith. Catalog wiring is done; final pass is moving remaining legacy command bodies out of `Program.cs`.
- [x] `--help` is understandable for an external user.
- [x] Exit codes are documented.
- [x] Public commands are not mixed with internal/experimental commands.

## 3. Packaging And Distribution Pipeline

Goal: make packaging reproducible and automatically verified.

Tasks:
- [x] Add a CI job for `dotnet pack`.
- [x] Add smoke coverage for `pack -> local tool install -> --help/doctor`.
- [x] Verify `.nupkg` contents in tests or CI.
- [x] Add smoke coverage for the standalone agent bundle.
- [x] Add checksum/manifest generation for the bundle.
- [x] Document the release process: preview, stable, rollback.

Acceptance criteria:
- [x] CI catches broken packages before publication.
- [x] The standalone bundle can be unpacked and run without repository source.
- [x] A new version can be released by following documented steps.

## 4. Documentation Cleanup

Goal: make documentation consistent and public-ready.

Tasks:
- [x] Synchronize `README.md`, `README.ru.md`, and `docs/user-guide/limitations.md`.
- [x] Remove stale claims such as "TypeScript is not supported" when the feature exists as experimental.
- [x] Split documentation into quick start, user guide, agent/autopilot guide, config/profile guide, limitations, and troubleshooting.
- [x] Remove, rewrite, or hide internal/rough documentation that would confuse public users.
- [x] Add real end-to-end examples.

Acceptance criteria:
- [x] A new user understands the tool in a few minutes.
- [x] Stable, preview, and experimental features are clearly labeled.
- [x] Documentation does not contradict actual CLI behavior.

## 5. Migration Quality Program

Goal: improve migration quality, not only generated-code compilation.

Tasks:
- [x] Build a prioritized list of top `UnsupportedAction` and TODO categories.
- [x] Create regression tickets/tests for each high-impact category.
- [x] Improve POM/helper recovery guidance through dashboard guardrails and source-truth tickets.
- [x] Improve selector evidence and confidence reporting.
- [x] Strengthen guardrails against unsafe suppression.
- [x] Improve reports so each TODO explains the root cause and next useful action.

Acceptance criteria:
- [x] Migration quality is measured with visible metrics.
- [x] Each new migration feature is expected to reduce a specific TODO/UnsupportedAction category; the dashboard now makes that measurable.
- [ ] Generated code compiles more often and contains fewer manual TODOs. Initial program/dashboard is implemented; actual category-reduction tickets should be executed next.

## 6. Agent Loop Hardening

Goal: make the agent loop reliable and resistant to premature stopping.

Tasks:
- [x] Keep one primary loop prompt.
- [x] Delete old prompts or mark them clearly as legacy.
- [x] Strengthen the rule against asking the user for routine continuation decisions under `CONTINUE_AUTONOMOUSLY`.
- [x] Strengthen the rule against editing migrator source code in artifact-only modes.
- [x] Add or refine a multi-agent loop: migrator, reviewer, verifier.
- [x] Add a stop-policy checklist.

Acceptance criteria:
- [x] The agent does not ask "continue?" without a real stop condition.
- [x] The agent does not edit migrator source code when the ticket is artifact-only.
- [x] Loops are usable as the primary recommended workflow.

## 7. Extensibility And Public API

Goal: make the tool easier for external contributors to extend.

Tasks:
- [x] Document `SourceFrontend` and `TargetBackend` contracts.
- [x] Document how to add a new source language or target backend.
- [x] Add or improve capability reporting for frontends/backends.
- [x] Provide examples for a mini plugin/custom profile.
- [x] Stabilize and version the adapter config schema.

Acceptance criteria:
- [x] An external contributor can add a frontend/backend by following the docs.
- [x] Stable APIs and internal APIs are clearly separated.
- [x] Config/profile format has explicit versioning.

## 8. Public Launch Pack

Goal: prepare a strong first public release.

Tasks:
- [x] Create a demo repository with Selenium tests and migrated output.
- [x] Add a short GIF/video or screenshot-based walkthrough.
- [x] Add a GitHub Actions example.
- [x] Publish a before/after migration report example.
- [x] Write a public roadmap from preview to stable.
- [x] Add issue templates for bugs, migration gaps, and profile requests.
- [x] Prepare NuGet/GitHub release notes.

Acceptance criteria:
- [x] A user can try the tool without help from the author.
- [x] The path is clear: install -> doctor -> migrate -> verify -> inspect report.
- [x] The public repository looks maintained and approachable.

## Suggested Sprint Order

1. Public Release Hygiene.
2. Packaging And Distribution Pipeline.
3. Documentation Cleanup.
4. CLI Productization.
5. Migration Quality Program.
6. Agent Loop Hardening.
7. Extensibility And Public API.
8. Public Launch Pack.
