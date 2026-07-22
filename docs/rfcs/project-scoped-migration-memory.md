# Project-scoped Migration Memory v1

Project memory lives only under `migration/state/memory/**`. No shared database is used. No cross-project/org knowledge pack is used. Memory is guidance, not authority: it cannot suppress assertions, weaken gates, change source scope, or promote adapter configuration automatically.

The CLI exposes `memory init`, `add`, `explain`, `doctor`, `summarize`, and `recall`. Decisions, warnings, selector evidence, anti-patterns, and gate lessons remain inspectable JSON/JSONL. Final gate must validate project memory before a handoff.

Standard runs remain project-local. Each full run may read applicable memory and emit reviewable config/memory deltas, but promotion always requires explicit review and fresh verification.

## Iteration 5: config delta merge and validation

Use `config merge-deltas` to create a reviewable candidate and `config validate-merge` before promotion. The candidate remains project-local and never edits the base config automatically.
