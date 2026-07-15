# Migration scaffolding

Migration scaffolding is a bounded structural fallback for project-specific helper and Page Object boundaries that are expensive to understand inside the bulk test migration. It is not suppression and it is not fake behavior.

## Product goal

The migrator optimizes for removing repetitive test-rewrite work, not for autonomously rebuilding every project dependency. Common Playwright actions, assertions, waits, simple POM members, and cheap deterministic helper side effects should still be migrated. Rare helper behavior that stalls after one bounded attempt can be isolated for later work.

## Protocol

```text
unknown helper/POM root
→ one bounded normal implementation attempt
→ CLI derives COMPLETED or NO_PROGRESS
→ COMPLETED: keep the real implementation
→ NO_PROGRESS: manager may select SCAFFOLD_CURRENT_ROOT
→ add one narrow explicit scaffold rule
→ regenerate and measure structural/runtime readiness separately
```

Configuration:

```json
{
  "ScaffoldMethods": [
    "TariffSettingsHelper.FindTariff"
  ],
  "ScaffoldMethodPatterns": [
    "TariffSettingsHelper.Select*"
  ]
}
```

A bare exact method applies only to an unqualified call. Qualified calls require their qualified member or owner pattern. Patterns must have an exact owner and may use a wildcard only in the final method segment.

## Generated behavior

The renderer replaces the selected invocation with an explicit `__MigratorScaffoldRuntime` call. It preserves result assignment and `await`, marks the code with `[MIGRATOR:SCAFFOLD]`, and throws `NotImplementedException` at runtime. It never returns a value that could let a test pass incorrectly.

## Guardrails

- remediation is `COMPLETED` only when the exact manager-selected root disappears from generated blocking TODOs; unrelated or partial cleanup cannot manufacture progress;
- one measured `NO_PROGRESS` for the exact manager candidate is required before the agent protocol may add a scaffold; the CLI rejects another normal remediation of the same root after `NO_PROGRESS`;
- a cascading `UNRESOLVED_SYMBOL` is not scaffold-eligible by itself: the system must identify a concrete project helper/POM root first, while its payoff includes dependent cascade TODOs;
- assertions, Selenium/Playwright APIs, selectors, waits, and arbitrary statements are ineligible;
- suppression and scaffolding cannot own the same exact method or pattern;
- catch-all patterns and wildcard owners are rejected by config validation;
- scaffold roots and scaffold-only tests are counted and bounded per wave;
- `ACCEPT_WAVE` is rejected while scaffolds remain; use `ACCEPT_WITH_SCAFFOLDING`;
- accepted scaffolding never changes `runtimeReady` to true.

The intended result is `MIGRATED_WITH_SCAFFOLDING`: the repetitive test structure is migrated and compile-connected, while a small, honest runtime-blocker list remains for isolated follow-up.
