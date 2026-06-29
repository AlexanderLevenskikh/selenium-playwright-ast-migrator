# Agent Handoff

This file is the first file a new agent should read after the loop instructions.

## Current status

Status: NOT_STARTED

## Last safe checkpoint

Run:

Commit/diff state:

Config version:

Output directory:

## What just happened


## What to do next


## Stop-policy checklist

Before accepting this handoff, verify `state/stop-policy-checklist.md` is current. If status is `CONTINUE_AUTONOMOUSLY`, the next agent should continue instead of asking the user whether to continue.

## What not to do

- Do not trust summaries without checking run artifacts.
- Do not ask the user whether to continue when the next status is `CONTINUE_AUTONOMOUSLY`.
- Do not add broad suppressions to reduce TODO count.
- Do not edit generated files as the final solution.
- Do not edit migrator source code in `migration-artifact` mode.
- Do not mark empty tests as runtime-ready.

## Open questions

