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

Use the prompt in `.agent-loops/kickoff-prompt.txt`.

Recommended prompt:

```text
Read all files in .agent-loops/.
Also read AGENTS.md and docs/autopilot-loop.md.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
Do not stop after partial progress.
Continue until the selected migration block is fixed and verified, or until the stop policy requires a real stop.

Current task:
<PASTE CURRENT BLOCK / ERROR / LOG / TODO CATEGORY HERE>

Use repository code, existing tests, snapshots, docs, CLI reports, and command output as the source of truth.
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
- `kickoff-prompt.txt`

## Verification commands

Run when applicable:

```bash
dotnet build
dotnet test Migrator.Tests
```

For a real source project, also run:

```bash
dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_TESTS> --out <VERIFY_OUT>
```

or the project-specific command that best matches the current task.

## Stop conditions

Stop only according to `.agent-loops/03-stop-policy.md`.

Typical valid stop statuses:

- `READY_FOR_ACCEPTANCE`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`
- `BLOCKED_BY_MISSING_INPUT`
- `MAX_ITERATIONS_REACHED`

Forbidden stop reasons:

- asking which implementation option the user prefers;
- asking whether to continue;
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
