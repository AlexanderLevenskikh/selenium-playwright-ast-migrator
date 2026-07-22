# Skill: efficient-frontier

Purpose: keep the orchestrator focused on judgment while bounded read-only scans or one delegated write task reduce token-heavy work without creating a second scheduler.

## Use when

- The task spans many files, runs, TODOs, logs, or generated artifacts.
- One or more independent read-only scans can run without sharing mutable state.
- The hard part is deciding strategy, not reading every line personally.

## Orchestration pattern

The frontier/orchestrator role owns:

- task framing;
- risk tradeoffs;
- decomposition into bounded read-only evidence scans and at most one active write task;
- selection from the installed executor, reviewer, and watchdog roles only;
- integration of findings;
- final verification strategy;
- reviewer/watchdog/final-gate handoff.

Helper roles own:

- file inventory;
- TODO/error/log reduction;
- local evidence extraction;
- one bounded implementation patch;
- one bounded research artifact.

## Rules

- Give each helper one input scope, one output artifact, and one stop condition.
- Do not create a queue of hidden execution batches. Only one delegated task may mutate files before the full run is repeated.
- Read-only scans may run independently, but helpers must not write shared machine state.
- Prefer run-local artifacts over global config edits.
- Integrate findings before starting the next run.
- Stop broad parallelism when state becomes contradictory or permission denied.

## Output expected from orchestrator

- decomposition decision;
- selected next bounded task;
- why this task is lower risk / higher value;
- integration evidence;
- remaining queue or blocker.
