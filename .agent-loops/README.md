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
Read .agent-loops/11-strict-ticket-boundaries.md especially carefully.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
Do not stop after partial progress.
Continue until the selected migration block is fixed and verified, or until the stop policy requires a real stop.

Migration scope:
- Source Selenium project: <SOURCE_SELENIUM_PROJECT_PATH>
- Target/generated Playwright project: <TARGET_PROJECT_OR_OUTPUT_PATH>
- Migrator config/profile: <CONFIG_OR_PROFILE_PATH>
- Compiled migrator tool, if compiled-tool-only mode: <COMPILED_TOOL_PATH_OR_EMPTY>
- Existing Playwright POM examples: <TARGET_POM_EXAMPLES_PATH_OR_EMPTY>
- Verify/orchestrate output directory: <OUTPUT_DIR>
- Latest migration board: <PATH_OR_EMPTY>
- Latest project verify report: <PATH_OR_EMPTY>

Allowed input paths:
- <ALLOWED_INPUT_PATH_1>
- <ALLOWED_INPUT_PATH_2>

Allowed write paths:
- <ALLOWED_WRITE_PATH_1>

Forbidden paths:
- <FORBIDDEN_OR_PARENT_PATHS>

Current task:
<PASTE CURRENT BLOCK / ERROR / LOG / TODO CATEGORY HERE>

If POMs/helpers are involved: run or inspect `index-pom` and `helper-inventory`; missing target POMs are not automatic blockers; generate POM scaffolds only in migration output or use raw locators from proven selectors; never invent selectors.

Do not ask “continue?”. Continue within the current ticket until it is completed, blocked, or validation is impossible.
Do not search outside allowed paths. Do not edit source files unless the repository source tree is listed as an allowed write path.

Use only allowed repository code, existing tests, snapshots, docs, CLI reports, migration board, source Selenium tests, target project conventions, and command output as the source of truth.
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
- `12-pom-helper-recovery-policy.md` — POM/helper source-truth, generated POM, helper-inventory, and raw locator fallback rules.
- `11-strict-ticket-boundaries.md` — hard path/ticket boundary rules for restricted workspaces and DLL/artifact tasks.
- `strict-ticket-prompt.txt` — copy-paste prompt for restricted ticket mode.
- `resume-prompt.txt` — restart prompt after interruption.
- `kickoff-prompt.txt` — copy-paste startup prompt.
