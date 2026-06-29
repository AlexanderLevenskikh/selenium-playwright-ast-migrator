# Multi-Agent Loop

Use this file when the user explicitly asks for several agents or when a large
migration block benefits from parallel review.

The default is still one agent. Multi-agent mode must keep a single coordinator
and strict ownership boundaries so agents do not fight over files or duplicate
work.

## Roles

### Coordinator

Owns the plan, state files, final handoff, and merge decision.

Responsibilities:

- read `.agent-loops/13-loop-contract.md`;
- define the shared goal, max iterations, check command, and exit condition;
- split work into non-overlapping sub-batches;
- assign allowed input/write paths per agent;
- keep `.agent-state/migrator-progress.md` current;
- run final verification;
- resolve conflicts by choosing the smallest safe change.

The coordinator is the only role that may combine results.

### Migration Agent

Works on one migration-artifact/config batch.

May edit only the allowed config/profile/output paths.

Must not edit migrator source code.

Examples:

- reduce one normalized TODO root cause;
- add a source-backed config mapping;
- inspect `EMPTY_TEST_AFTER_SUPPRESSION`;
- run `helper-inventory` and classify one helper family.

### Verifier Agent

Reviews outputs and checks claims.

May run commands and read reports.

May make only tiny obvious fixes if the current prompt allows edits.

Must not silently broaden scope.

### Migrator-Code Agent

Allowed only when explicitly requested with:

```text
Mode: migrator-code
Repository source edits are allowed.
```

Works on deterministic migrator engine changes with tests.

Must not touch project-specific migration config unless that path is explicitly
allowed.

## Parallelization rules

Parallel agents may work only when their write paths do not overlap.

Good parallel splits:

- one agent inspects helper-inventory while another verifies config-validate;
- one agent handles `page.Pagination.Forward`, another handles table/list TODOs;
- one verifier reviews artifacts while one migration agent prepares a config
  batch in a separate output directory.

Bad parallel splits:

- two agents editing the same `adapter-config.json`;
- one agent editing migrator code while another relies on the old binary;
- several agents running broad refactors;
- agents writing generated files by hand.

## Required handoff from each sub-agent

Each sub-agent must report:

- role;
- assigned goal;
- allowed input paths inspected;
- files changed;
- commands run;
- metrics before/after;
- evidence used;
- remaining risks;
- exact next action.

The coordinator must reject handoffs that do not state path boundaries or
validation evidence.

## Conflict policy

When two agents produce conflicting recommendations:

1. prefer command/report evidence over claims;
2. prefer source/POM/helper evidence over name-based guesses;
3. prefer config/output changes over migrator-code changes in migration mode;
4. prefer the smaller reversible batch;
5. if still ambiguous, create a ticket-ready blocker rather than guessing.

## Final verification

The coordinator must run final checks after combining work:

```powershell
dotnet build
dotnet test Migrator.Tests/Migrator.Tests.csproj
```

For migration-artifact loops, also run the strongest available migration check,
for example `config-validate`, `migrate`, `verify-project`, `explain-todo`, or
`migration-board` with the concrete paths from the task.
