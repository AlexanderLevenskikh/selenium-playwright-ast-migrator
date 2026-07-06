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

## Project-scoped only

This feature deliberately stops at project-local migration memory. There is no cross-project/org knowledge pack, no shared registry, and no automatic promotion outside the current `migration/**` workspace.

## Future: divide-and-conquer wavefront

Project-local memory is the foundation for divide-and-conquer wavefront migration. The wavefront feature remains scoped to the same migration workspace: planned waves may read project memory, emit config/memory deltas, and produce evidence, but they do not publish org-wide knowledge packs.

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
- Wave planning cannot promote memory entries or change adapter config.
- Wave execution must emit `config-delta`, `memory-delta`, and reviewable evidence before anything is promoted.

## Iteration 4: bounded wave run workspace

`migration run-wave` materializes one planned wave as a bounded workspace. It copies only the files touched by the selected wave into `source-scope/`, prepares an isolated `generated/` folder, writes project-local deltas, and emits scripts for the existing migration pipeline.

```bash
selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001
```

Artifacts:

- `input-scope.json` — exact wave id, files, tests, copied files, missing files, source scope, generated output path.
- `source-scope/` — copied source files for this bounded wave only.
- `generated/` — placeholder or generated output if `--execute-migrate true` was used.
- `config-delta.json` — observed/reviewable delta shell with safety invariants; never merged automatically.
- `memory-delta.jsonl` — wave-local lessons/warnings; guidance, not authority.
- `run-summary.md` / `run-summary.json` — human-readable wave summary and review checklist.
- `run-migrate.sh` / `run-migrate.ps1` — explicit migrate command for this wave.
- `wave-status.json` — prepared/completed/failed/incomplete status.

Safety boundary:

- `run-wave` never promotes memory automatically.
- `run-wave` never edits the original source tree.
- `run-wave` never merges `config-delta.json` into `adapter-config.json`.
- `config-delta.json` starts as `observed` and `requiresReviewerBeforeMerge`.
- Assertions must not be suppressed.
- POM uncertainty must remain reviewable until target mapping exists.


## Iteration 5: config delta merge and validation

Wave-local `config-delta.json` files are not applied directly to `adapter-config.json`. They are merged into a candidate config and validated as a separate, reviewable step:

```bash
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

Artifacts:

- `migration/config-merge/adapter-config.merged.json` — candidate config only.
- `merge-report.md` / `merge-report.json` — applied changes, skipped duplicates, warnings, and conflicts.
- `validate-merge-report.md` / `validate-merge-report.json` — validation status and safety findings.
- `conflicts.jsonl` — machine-readable conflict list for Reviewer/Watchdog.

Safety boundary:

- `config merge-deltas` never edits the base `adapter-config.json`.
- `config validate-merge` never promotes the candidate automatically.
- Assertion suppression and over-suppression are forbidden shortcuts.
- Same stable key with different content is a conflict, not an automatic overwrite.
- POM-like broad suppression is at least a warning and should remain reviewable until target mapping exists.
- Final Gate checks `config-delta-merge` when a `migration/config-merge` candidate exists.

## Iteration 6: dashboard and evidence polish

The final MVP polish connects the project-scoped memory and divide-and-conquer artifacts to the existing `report serve` review surface.

`report serve` now exposes a **Wavefront / memory / config-merge snapshot**:

- project-scoped memory presence and entry counts;
- wavefront plan presence, total waves, completed wave runs, and next wave candidates;
- config merge status, including whether `validate-merge-report` exists and whether `conflicts.jsonl` is non-empty;
- suggested next commands such as `memory doctor`, `migration run-wave`, `config merge-deltas`, or `config validate-merge`.

The dashboard remains read-only. It does not promote memory entries, apply candidate config, mark waves complete, or write decisions from the browser.

The dashboard evidence zip can include nearby workspace artifacts from `state/memory`, `plan`, and `config-merge`. The manifest includes `ProjectScopedMemoryAndWavefrontArtifactsIncluded` so reviewers can see that the evidence pack contains project-local migration state.
