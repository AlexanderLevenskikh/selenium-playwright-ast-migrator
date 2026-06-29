# Public Release TODO

Temporary planning file. Remove this document after the public-release work is implemented or moved into real issues.

## 1. Public Release Hygiene

Goal: prepare the package for safe public publication.

Tasks:
- Remove `.agent-state` from NuGet/dotnet-tool package contents.
- Add public package metadata: license, repository URL, project URL, icon, release notes.
- Replace `Company=Internal` and other internal wording.
- Verify that local artifacts, agent state, temp files, and private docs are not packed.
- Add or finalize `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, and `CHANGELOG.md`.

Acceptance criteria:
- `.nupkg` contains only public-safe files.
- `dotnet tool install` works from the local `.nupkg`.
- Package metadata is ready for NuGet/GitHub release.

## 2. CLI Productization

Goal: make the CLI easier to understand, test, and extend.

Tasks:
- Split the large `Program.cs` mode handling into dedicated command classes.
- Introduce a single command registry/catalog.
- Standardize command options, help output, and exit codes.
- Add snapshot tests for `--help` and key command help pages.
- Separate commands into stable, experimental, and internal groups.

Acceptance criteria:
- A new CLI command can be added without editing a large monolith.
- `--help` is understandable for an external user.
- Exit codes are documented.
- Public commands are not mixed with internal/experimental commands.

## 3. Packaging And Distribution Pipeline

Goal: make packaging reproducible and automatically verified.

Tasks:
- Add a CI job for `dotnet pack`.
- Add smoke coverage for `pack -> local tool install -> --help/doctor`.
- Verify `.nupkg` contents in tests or CI.
- Add smoke coverage for the standalone agent bundle.
- Add checksum/manifest generation for the bundle.
- Document the release process: preview, stable, rollback.

Acceptance criteria:
- CI catches broken packages before publication.
- The standalone bundle can be unpacked and run without repository source.
- A new version can be released by following documented steps.

## 4. Documentation Cleanup

Goal: make documentation consistent and public-ready.

Tasks:
- Synchronize `README.md`, `README.ru.md`, and `docs/user-guide/limitations.md`.
- Remove stale claims such as "TypeScript is not supported" when the feature exists as experimental.
- Split documentation into quick start, user guide, agent/autopilot guide, config/profile guide, limitations, and troubleshooting.
- Remove, rewrite, or hide internal/rough documentation that would confuse public users.
- Add real end-to-end examples.

Acceptance criteria:
- A new user understands the tool in a few minutes.
- Stable, preview, and experimental features are clearly labeled.
- Documentation does not contradict actual CLI behavior.

## 5. Migration Quality Program

Goal: improve migration quality, not only generated-code compilation.

Tasks:
- Build a prioritized list of top `UnsupportedAction` and TODO categories.
- Create regression tickets/tests for each high-impact category.
- Improve POM/helper recovery.
- Improve selector evidence and confidence reporting.
- Strengthen guardrails against unsafe suppression.
- Improve reports so each TODO explains the root cause and next useful action.

Acceptance criteria:
- Migration quality is measured with visible metrics.
- Each new migration feature reduces a specific TODO/UnsupportedAction category.
- Generated code compiles more often and contains fewer manual TODOs.

## 6. Agent Loop Hardening

Goal: make the agent loop reliable and resistant to premature stopping.

Tasks:
- Keep one primary loop prompt.
- Delete old prompts or mark them clearly as legacy.
- Strengthen the rule against asking the user for routine continuation decisions under `CONTINUE_AUTONOMOUSLY`.
- Strengthen the rule against editing migrator source code in artifact-only modes.
- Add or refine a multi-agent loop: migrator, reviewer, verifier.
- Add a stop-policy checklist.

Acceptance criteria:
- The agent does not ask "continue?" without a real stop condition.
- The agent does not edit migrator source code when the ticket is artifact-only.
- Loops are usable as the primary recommended workflow.

## 7. Extensibility And Public API

Goal: make the tool easier for external contributors to extend.

Tasks:
- Document `SourceFrontend` and `TargetBackend` contracts.
- Document how to add a new source language or target backend.
- Add or improve capability reporting for frontends/backends.
- Provide examples for a mini plugin/custom profile.
- Stabilize and version the adapter config schema.

Acceptance criteria:
- An external contributor can add a frontend/backend by following the docs.
- Stable APIs and internal APIs are clearly separated.
- Config/profile format has explicit versioning.

## 8. Public Launch Pack

Goal: prepare a strong first public release.

Tasks:
- Create a demo repository with Selenium tests and migrated output.
- Add a short GIF/video or screenshot-based walkthrough.
- Add a GitHub Actions example.
- Publish a before/after migration report example.
- Write a public roadmap from preview to stable.
- Add issue templates for bugs, migration gaps, and profile requests.
- Prepare NuGet/GitHub release notes.

Acceptance criteria:
- A user can try the tool without help from the author.
- The path is clear: install -> doctor -> migrate -> verify -> inspect report.
- The public repository looks maintained and approachable.

## Suggested Sprint Order

1. Public Release Hygiene.
2. Packaging And Distribution Pipeline.
3. Documentation Cleanup.
4. CLI Productization.
5. Migration Quality Program.
6. Agent Loop Hardening.
7. Extensibility And Public API.
8. Public Launch Pack.
