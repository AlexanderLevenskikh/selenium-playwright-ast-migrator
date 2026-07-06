# RFC: Project-scoped Migration Memory v1

## Summary

Project-scoped migration memory is an inspectable set of JSON/JSONL files under `migration/state/memory/**`. It lets a long migration remember decisions, warnings, rejected approaches, final-gate lessons, and selector evidence across bounded runs without relying on chat memory or global organization knowledge packs.

This RFC intentionally scopes memory to one migration workspace. Distributed team/org knowledge packs are a later feature.

## Goals

- Make each next bounded action cheaper by preserving project-local decisions and final-gate lessons.
- Keep the memory reviewable in PRs and evidence packs.
- Give Supervisor, Executor, Reviewer, Watchdog, and Final Gate a shared state surface.
- Prevent memory from becoming a loophole for assertion suppression, over-suppression, or invented selectors.

## Non-goals

- No shared database.
- No cross-project/org knowledge pack.
- No embeddings/vector search in v1.
- No automatic promotion of memory to global rules.

## Layout

```text
migration/state/memory/
  README.md
  project-profile.json
  memory-summary.md
  decisions.jsonl
  warnings.jsonl
  antipatterns.jsonl
  final-gate-lessons.jsonl
  user-notes.jsonl
  selector-map.json
  recall-index.json
  config-deltas/
```

## Memory kinds

- `decision`: a durable project decision.
- `preference`: preferred style or strategy.
- `constraint`: what the migration must not do.
- `warning`: a project-local caution for future runs.
- `antipattern`: a bad path already observed or forbidden.
- `final-gate-lesson`: a lesson from gate/reviewer/watchdog evidence.
- `user-note`: miscellaneous user-provided context.

## CLI MVP

```bash
selenium-pw-migrator memory init --workspace migration
selenium-pw-migrator memory add --kind decision "Keep POM unresolved until target mapping exists"
selenium-pw-migrator memory explain --workspace migration
selenium-pw-migrator memory doctor --workspace migration --format both --out migration/memory-doctor
selenium-pw-migrator memory summarize --workspace migration --run migration/runs/run-010
```

## Agent lifecycle integration

Before planning, agents read:

- `migration/state/memory/memory-summary.md`;
- active decisions, warnings, antipatterns, and final-gate lessons;
- `selector-map.json` only as source-backed evidence.

After a bounded action, agents record:

- decisions;
- warnings;
- rejected approaches;
- final-gate lessons;
- config deltas under `state/memory/config-deltas/`.

## Safety rules

Memory is guidance, not authority.

- Memory cannot justify assertion suppression.
- Memory cannot justify hiding over-suppressed user interactions.
- Selector knowledge must have evidence before reuse.
- POM uncertainty stays reviewable until target mapping exists.
- TODO reduction is not automatically progress.
- TODO increase is not automatically regression.

## Final-gate integration

Final gate must validate project memory when present:

- JSON/JSONL parseability;
- required fields for memory entries;
- no active memory that allows assertion suppression;
- selector-map entries have `sourceExpression`, `targetLocator`, and `evidence[]`;
- memory doctor output can be attached as evidence.

## Future: divide-and-conquer wavefront

A later iteration can add `migration plan --strategy wavefront`, `run-wave`, config deltas, merge conflicts, and trust promotion. This RFC is the foundation: project-local memory first, progressive migration second.

## Iteration 3: read-only divide-and-conquer wave planning

The next local-only step is a read-only `migration` command family. It does not run migration waves yet; it creates a bounded plan that agents can use as state instead of asking the user what to do next.

```bash
selenium-pw-migrator migration inventory --input ./SeleniumTests --out migration/plan
selenium-pw-migrator migration cluster --input ./SeleniumTests --out migration/plan
selenium-pw-migrator migration plan --strategy wavefront --input ./SeleniumTests --workspace migration --out migration/plan
selenium-pw-migrator migration plan show --plan migration/plan
```

Artifacts:

- `inventory.json` / `inventory.md` — Selenium-like test methods, files, clusters, tags, risks, and representative scores.
- `clusters.json` / `clusters.md` — project-local clusters such as Auth, Table, SearchFilter, Modal, POM-heavy, Wait-heavy.
- `waves.json` / `plan.md` — representative waves followed by cluster expansion waves.
- `selected-tests.txt` — deterministic list of tests in planned wave order.
- `memory-recall.md` — instructions to run `memory explain`, `memory doctor`, and `memory recall --file` before turning a wave into a bounded agent task.
- `next-commands.md` — safe next commands.

Safety boundary:

- `migration plan` is read-only.
- `run-wave` is intentionally future work.
- Wave planning cannot promote memory entries or change adapter config.
- Any future wave execution must emit `config-delta`, `memory-delta`, reviewer findings, watchdog findings, and final-gate evidence.
