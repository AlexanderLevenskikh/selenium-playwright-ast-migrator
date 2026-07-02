> **Legacy/background note:** Do not use this document as the current guarded OpenCode Desktop launch procedure. For current migration-agent runs, start with `docs/guarded-opencode-desktop-runbook.ru.md`.

# Agent and autopilot guide

The Migrator can be used manually or by a coding agent. Agent mode is useful when a migration has many repeated TODO/root-cause categories and the agent can run tests, edit config, verify output, and continue without asking for routine continuation decisions.

## When to use an agent

Use an agent when:

- The initial migration already produces reports and generated files.
- The next work item is a repeated, measurable category such as unmapped locators, helper wrappers, table/list access, or compile errors.
- The agent has access to source Selenium tests, PageObjects/helpers, target Playwright examples, config/profile files, and verification commands.

Do not use an agent as a substitute for source truth. The agent must not invent selectors or silently suppress unsafe code.


## Primary prompt and hardening rules

Use `.agent-loops/kickoff-prompt.txt` as the single primary prompt for new autopilot runs. Other prompts are secondary wrappers for resume, review, strict-ticket, or migration-kit workflows. They must not conflict with the primary loop contract.

Hard rules for public workflows:

- if status is `CONTINUE_AUTONOMOUSLY`, the agent continues without asking “continue?”;
- `migration-artifact` mode cannot edit migrator repository source code;
- compiled-tool-only mode cannot search for migrator source code;
- before any stop/handoff, apply `.agent-loops/15-stop-policy-checklist.md`;
- multi-agent runs must use `.agent-loops/14-multi-agent-loop.md` with one coordinator and non-overlapping write paths.

## Core loop

1. Read repository and migration-kit rules.
2. Pick one small root-cause category from reports or the migration board.
3. Find source truth in Selenium PageObjects/helpers or target Playwright code.
4. Change profile/config or migrator behavior.
5. Run focused tests and migration verification.
6. Record evidence and continue to the next safe batch unless the stop policy is triggered.

## Recommended entry points

- [Autopilot loop](autopilot-loop.md)
- [Agent command set](agent-command-set.md)
- [Agent safety](agent-safety.md)
- [Agent config guidelines](agent-config-guidelines.md)
- [Agent playbooks](agent-playbooks/README.md)
- [Migration kit MVP4](migration-kit-mvp4.md)

## Migration kit workflow

```bash
selenium-pw-migrator kit init --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit doctor --workspace migration
selenium-pw-migrator kit next-ticket --workspace migration --input migration/run-001
```

The kit creates prompts, state files, profile placeholders, and safety checklists. It does not replace verification.

## Stop conditions

An agent should stop only for real blockers, for example:

- Required source or target files are missing.
- The next step would require guessing selectors or business behavior.
- Build/test tools are unavailable and no useful static verification remains.
- A safety rule prevents the requested edit.

A green build is a checkpoint, not automatically the end of a migration. If actionable TODOs, unsupported actions, missing mappings, or runtime candidates remain, continue with the next small batch.
