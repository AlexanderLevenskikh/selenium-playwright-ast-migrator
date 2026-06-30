# Public roadmap

This roadmap describes the path from public preview to a stable 1.0 release.

## Preview: 0.6.x

Focus: safe public adoption by early users.

- Keep the stable path focused on Selenium C# → Playwright .NET, with NUnit as the default target framework and xUnit as a supported target framework.
- Improve packaging, docs, demo assets, and issue triage.
- Use `migration-quality-dashboard.*` to prioritize high-impact mapping work.
- Collect migration gaps through GitHub issue templates.
- Keep experimental Java, Python, and Playwright TypeScript paths clearly labeled.

## Beta: 0.7.x–0.9.x

Focus: stronger migration quality and project-level confidence.

- Move more CLI modes out of legacy `Program.cs` blocks into command classes.
- Reduce the top recurring `UnsupportedAction` and TODO categories from real projects.
- Expand POM/helper recovery based on source-truth evidence.
- Improve TypeScript target parity and verification.
- Add more public examples for common Selenium PageObject patterns.
- Stabilize more public contracts only after they are used by external contributors.

## Stable: 1.0

Focus: stable contracts and predictable upgrades.

- Freeze `adapter-config/v1` compatibility rules or publish `adapter-config/v2` with migration docs.
- Document stable `ISourceFrontend` and `ITargetBackend` extension boundaries.
- Maintain backward-compatible CLI behavior for stable modes.
- Publish a stable release checklist and support policy.
- Keep experimental source/target combinations behind explicit labels until they meet stable quality bars.

## Not a goal

The project will not promise perfect one-click migration. Selenium suites encode too much project-specific knowledge in helpers, PageObjects, waits, and selectors. The tool should make that knowledge visible, measurable, and safely reusable.
