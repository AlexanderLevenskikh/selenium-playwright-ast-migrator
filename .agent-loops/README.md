# Migrator Agent Loops

This folder contains local agent-loop instructions for the Selenium C# → Playwright .NET migrator.

The goal is to make the agent work autonomously:

- pick the next migration gap;
- make a safe technical decision;
- add/update tests;
- fix the migrator;
- run verification;
- continue until the selected block is done or a real stop condition is reached.

The user should not be asked to choose between implementation options.
The user is responsible for final acceptance, not for ordinary engineering decisions.

## Recommended kickoff prompt

```text
Read all files in .agent-loops/.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
Do not stop after partial progress.
Continue until the selected migration block is fixed and verified, or until the stop policy requires a real stop.

Migration scope:
- Source Selenium project: <SOURCE_SELENIUM_PROJECT_PATH>
- Target/generated Playwright project: <TARGET_PROJECT_OR_OUTPUT_PATH>
- Migrator config/profile: <CONFIG_OR_PROFILE_PATH>
- Verify/orchestrate output directory: <OUTPUT_DIR>
- Latest migration board: <PATH_OR_EMPTY>
- Latest project verify report: <PATH_OR_EMPTY>

Current task:
<PASTE CURRENT BLOCK / ERROR / LOG / TODO CATEGORY HERE>

Use repository code, existing tests, snapshots, docs, CLI reports, migration board, source Selenium tests, target project conventions, and command output as the source of truth.
```

## Files

- `00-context.md` — project-specific migrator context.
- `01-autopilot-loop.md` — main implementation loop.
- `02-guardrails.md` — hard technical rules.
- `03-stop-policy.md` — when the agent may and may not stop.
- `04-work-queue.md` — how to choose the next work item.
- `05-verifier-loop.md` — independent verification pass.
- `06-report-format.md` — required final report format.
- `07-ticket-needed-template.md` — local template for cases that really need a ticket.
- `08-continuation-rule.md` — prevents stopping after green compile/project verify when migration work remains.
- `09-continue-after-compile-fix-prompt.txt` — prompt for continuing after compile-fix milestone.
- `10-state-and-resume.md` — state files and resume protocol for long-running loops.
- `resume-prompt.txt` — restart prompt after interruption.
- `kickoff-prompt.txt` — copy-paste startup prompt.
