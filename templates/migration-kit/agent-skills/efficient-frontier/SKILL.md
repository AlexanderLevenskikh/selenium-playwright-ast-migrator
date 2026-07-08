# Skill: efficient-frontier

Purpose: keep the strongest/most expensive model or role focused on judgment while bounded helper roles perform token-heavy work.

## Use when

- The task spans many files, waves, TODOs, logs, or generated artifacts.
- Multiple independent scans can run without sharing mutable state.
- The hard part is deciding strategy, not reading every line personally.

## Orchestration pattern

The frontier/orchestrator role owns:

- task framing;
- risk tradeoffs;
- decomposition into bounded work packets;
- selection of helpers;
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
- Do not let helpers write shared machine state unless their role owns it.
- Prefer wave-local artifacts over global config edits.
- Integrate findings before starting the next wave.
- Stop broad parallelism when state becomes contradictory or permission denied.

## Output expected from orchestrator

- decomposition decision;
- selected next bounded task;
- why this task is lower risk / higher value;
- integration evidence;
- remaining queue or blocker.
