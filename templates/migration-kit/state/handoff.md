# Agent Handoff

Read this file before continuing a standard migration run.

## Current status

Status: NOT_STARTED

## Latest verified run

Run:

Commit/diff state:

Config version:

Output directory:

Project verify:

Final gate:

## What just happened


## What to do next


## Required checks before accepting the handoff

- Confirm the referenced run directory exists.
- Read the orchestration, generated, and project-verify reports.
- Confirm `state/stop-policy-checklist.md` is current.
- Do not treat a summary or copied validation file as evidence.

## What not to do

- Do not partition the configured source into hidden execution batches.
- Do not add broad suppressions to reduce TODO count.
- Do not edit generated files as the final solution.
- Do not edit migrator source code in `migration-artifact` mode.
- Do not mark empty tests as runtime-ready.
- Do not continue indefinitely: apply at most one bounded repair before a complete rerun and handoff.

## Open questions

