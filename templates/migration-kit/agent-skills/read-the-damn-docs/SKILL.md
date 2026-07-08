# Skill: read-the-damn-docs

Purpose: prevent stale model-memory guesses when current external APIs, packages, CLIs, frameworks, or migration guides matter.

## Use when

The task touches any of these:

- dependency upgrades;
- package-manager behavior;
- framework/SDK/CLI configuration;
- Playwright, Selenium, browser, CI image, auth, cloud, or provider behavior;
- security/billing/deployment limits;
- major-version migration rules;
- repo-specific contracts that may be documented in local docs.

## Evidence order

1. Official migration guide, changelog, API docs, release notes, or SDK docs.
2. Installed package metadata, lockfile, generated types, source code, or vendor docs available in the repo.
3. Existing project examples and tests.
4. Only then infer.

If web access is denied or unavailable, say so and use local evidence. If authoritative external docs are required to safely continue, stop with `BLOCKED_BY_DOCS_REQUIRED` and write the exact docs needed.

## Required notes

Record:

- installed/current version when visible;
- target/recommended version when visible;
- docs or local evidence consulted;
- migration rule that affected the change;
- verification that checks the rule.

## Anti-patterns

- Do not rely on remembered package behavior for fast-moving libraries.
- Do not update a major version without identifying the migration guide or local equivalent.
- Do not claim a docs pass when only a README snippet or old blog post was read.
- Do not use network commands if policy denies them.
