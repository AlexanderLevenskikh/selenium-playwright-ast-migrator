> **Legacy/background note:** Do not use this document as the current guarded OpenCode Desktop launch procedure. For current migration-agent runs, start with `docs/guarded-opencode-desktop-runbook.ru.md`.

# Autopilot Loop

This repository uses an autopilot-first workflow for developing the migrator.

The agent is expected to work in a closed loop:

```text
pick migration gap
→ classify
→ add/update regression test
→ implement smallest safe fix
→ run build/tests/verify
→ read output
→ continue or stop by policy
```

The agent should not stop after partial progress and should not ask the user to choose between implementation options.

## Startup

Use `.agent-loops/kickoff-prompt.txt` as the **single primary loop prompt**. Resume/strict-ticket prompts are secondary wrappers and must defer to the same loop contract.

Recommended prompt:

```text
Read all files in .agent-loops/.
Also read AGENTS.md, docs/autopilot-loop.md, .agent-loops/12-pom-helper-recovery-policy.md, and .agent-loops/15-stop-policy-checklist.md.

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

Current task:
<PASTE CURRENT BLOCK / ERROR / LOG / TODO CATEGORY HERE>

Use repository code, existing tests, snapshots, docs, CLI reports, migration board, source Selenium tests, target project conventions, and command output as the source of truth.
```

## What changed compared to the old workflow

The old workflow used human checkpoints between iterations.

The autopilot workflow removes that default checkpoint.

The agent must continue when the current status is `CONTINUE_AUTONOMOUSLY`.

The user is responsible for final acceptance, not for ordinary technical choices.

## Required files

The source of truth for the loop is `.agent-loops/`:

- `00-context.md`
- `01-autopilot-loop.md`
- `02-guardrails.md`
- `03-stop-policy.md`
- `04-work-queue.md`
- `05-verifier-loop.md`
- `06-report-format.md`
- `07-ticket-needed-template.md`
- `kickoff-prompt.txt` — primary prompt for new runs
- `15-stop-policy-checklist.md`

## POM/helper recovery requirement

When a migration block contains PageObject expressions, missing target POM classes, source-only POM roots, or project helper wrappers, read `.agent-loops/12-pom-helper-recovery-policy.md`.

Required order:

1. Run or inspect `index-pom` for Selenium PageObject selector evidence.
2. Run or inspect `helper-inventory` for helper/POM wrapper semantics.
3. Use existing target Playwright POMs only as style/convention examples.
4. If target POM coverage is missing but Selenium selector evidence exists, generate POM scaffold/members in the migration output path or use raw Playwright locators from proven selectors.
5. Stop with `TICKET_NEEDED` only when selector/helper semantics cannot be proven from allowed inputs or the needed write path is forbidden.

Do not invent selectors. `ByTId("value")` and equivalent source POM selector factories are source truth; PageObject names are not selectors.

## Compiled-tool-only command rule

If a compiled migrator tool path is provided, run that tool directly and do not search for `Migrator.Cli`, `.sln`, `.csproj`, or migrator source code. Use repository commands only when the repository source is explicitly inside allowed input/write paths.

## Verification commands

Run when applicable:

```bash
dotnet build
dotnet test Migrator.Tests/Migrator.Tests.csproj
```

For a real source project, also run:

```bash
dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_TESTS> --out <VERIFY_OUT>
```

or the project-specific command that best matches the current task.

## Stop conditions

Stop only according to `.agent-loops/03-stop-policy.md` and after applying `.agent-loops/15-stop-policy-checklist.md`.

Typical valid stop statuses:

- `READY_FOR_ACCEPTANCE`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`
- `BLOCKED_BY_MISSING_INPUT`
- `MAX_ITERATIONS_REACHED`

Forbidden stop reasons:

- asking which implementation option the user prefers;
- asking whether to continue, including when status is `CONTINUE_AUTONOMOUSLY`;
- stopping after partial progress;
- stopping because there are multiple reasonable designs.

## Verifier pass

After an implementation loop, run `.agent-loops/05-verifier-loop.md` when you need independent review.

The verifier trusts only:

- git diff;
- build/test output;
- snapshots;
- generated reports;
- compile-smoke / verify results.

The verifier does not trust the implementer’s claims.


## Checkpoint is not completion

A green build, green `verify-project`, or zero compile errors is a safe checkpoint, not the end of the migration, unless the user explicitly requested only compile/build work.

When compile-fix completes, use two statuses if needed:

```text
Compile-fix batch: READY_FOR_ACCEPTANCE
Overall migration loop: CONTINUE_AUTONOMOUSLY
```

If the latest migration board still has actionable TODOs, missing mappings, unresolved symbols, unsupported actions, empty tests, or runtime candidates, the agent should continue with the next batch.

## Migration-quality trade-offs

The agent must not stop merely because the next phase involves TODO reduction or mapping/suppression trade-offs.

The correct behavior is to choose a small safe reversible batch, for example:

- assess `EMPTY_TEST_AFTER_SUPPRESSION`;
- map one source-backed repeated expression;
- classify one unsupported helper-method family;
- run one runtime smoke candidate.

Stop only when the trade-off requires product/business semantics, unavailable source truth, destructive action, or another hard stop condition.


## One primary prompt

The canonical new-run prompt is `.agent-loops/kickoff-prompt.txt`. Older examples and kit prompts should either point to that prompt or act as bounded wrappers for resume/review/ticket workflows. They must not reintroduce human checkpoint language such as “Should I continue?” or allow migrator source edits in `migration-artifact` mode.

Before any final stop or handoff, the agent should apply `.agent-loops/15-stop-policy-checklist.md`. If the checklist does not prove a hard stop and status is `CONTINUE_AUTONOMOUSLY`, the agent continues.
