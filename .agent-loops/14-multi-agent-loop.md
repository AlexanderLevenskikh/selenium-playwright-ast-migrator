# Multi-Agent Loop

Use this file when the user explicitly asks for several agents or when a large migration block benefits from parallel review. The default workflow is still one primary loop prompt: `.agent-loops/kickoff-prompt.txt`.

Multi-agent mode must keep a single coordinator and strict ownership boundaries so agents do not fight over files, duplicate work, or mix migration-artifact changes with migrator-source changes.

## Non-negotiable mode boundary

Every sub-agent must inherit the parent loop mode and path contract from `13-loop-contract.md`.

- In `migration-artifact` mode, no sub-agent may edit migrator repository source code.
- In compiled-tool-only mode, no sub-agent may search for `Migrator.Cli`, `.sln`, `.csproj`, or migrator source folders.
- `Migrator-Code Agent` may exist only when the parent prompt explicitly says `Mode: migrator-code` and lists the repository source tree as an allowed write path.
- If a sub-agent discovers that source edits are needed but source edits are forbidden, it writes a source-change ticket candidate and returns control to the coordinator.

## Roles

### Coordinator

Owns the plan, state files, final handoff, and merge decision.

Responsibilities:

- read `.agent-loops/13-loop-contract.md` and `.agent-loops/15-stop-policy-checklist.md`;
- define the shared goal, max iterations, check command, and exit condition;
- split work into non-overlapping sub-batches;
- assign allowed input/write paths per agent;
- keep `.agent-state/migrator-progress.md` current;
- run final verification;
- resolve conflicts by choosing the smallest safe reversible change.

The coordinator is the only role that may combine results. The coordinator must reject any sub-agent result that lacks path boundaries, evidence, or verification.

### Migration Agent

Works on one migration-artifact/config batch.

May edit only the allowed config/profile/output/report paths.

Must not edit migrator source code.

Examples:

- reduce one normalized TODO root cause;
- add a source-backed config mapping;
- inspect `EMPTY_TEST_AFTER_SUPPRESSION`;
- run `helper-inventory` and classify one helper family;
- generate POM candidates only inside allowed migration/output paths.

### Verifier Agent

Reviews outputs and checks claims.

May run commands and read reports.

May make only tiny obvious fixes if the current prompt allows edits and the write path belongs to the verifier.

Must not silently broaden scope.

The verifier trusts only:

- git diff or file diff;
- build/test/verify output;
- generated reports;
- snapshots;
- source/POM/helper evidence.

### Migrator-Code Agent

Allowed only when explicitly requested with:

```text
Mode: migrator-code
Repository source edits are allowed.
```

Works on deterministic migrator engine changes with tests.

Must not touch project-specific migration config unless that path is explicitly allowed.

Must add or update regression tests for behavior changes when feasible.

## Parallelization rules

Parallel agents may work only when their write paths do not overlap.

Good parallel splits:

- one agent inspects helper-inventory while another verifies config-validate;
- one agent handles `page.Pagination.Forward`, another handles table/list TODOs;
- one verifier reviews artifacts while one migration agent prepares a config batch in a separate output directory.

Bad parallel splits:

- two agents editing the same `adapter-config.json`;
- one agent editing migrator code while another relies on the old binary;
- several agents running broad refactors;
- agents writing generated files by hand;
- one agent updating suppressions while another updates MethodSemantics for the same helper family.

## Sub-agent assignment template

Each assignment must include:

```text
Role: <Coordinator | Migration Agent | Verifier Agent | Migrator-Code Agent>
Inherited mode: <migration-artifact | migrator-code | strict-ticket>
Goal: <one concrete sub-batch>
Allowed input paths:
- <path>
Allowed write paths:
- <path or none>
Forbidden paths:
- parent directories outside allowed paths
- migrator repository source code unless inherited mode is migrator-code and source tree is allowed
Required evidence:
- <reports/POM/helper/logs>
Required checks:
- <commands or static checks>
Stop-policy checklist: apply .agent-loops/15-stop-policy-checklist.md before handoff
```

## Required handoff from each sub-agent

Each sub-agent must report:

- role;
- assigned goal;
- inherited mode;
- allowed input paths inspected;
- allowed write paths used;
- forbidden paths avoided;
- files changed;
- commands run;
- metrics before/after;
- evidence used;
- remaining risks;
- stop-policy checklist result;
- exact next action.

The coordinator must reject handoffs that do not state path boundaries or validation evidence.

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

For migration-artifact loops, also run the strongest available migration check, for example `config-validate`, `migrate`, `verify-project`, `explain-todo`, or `migration-board` with the concrete paths from the task.

Before the final response, the coordinator must apply `.agent-loops/15-stop-policy-checklist.md`. If the status is `CONTINUE_AUTONOMOUSLY`, it should continue instead of asking the user whether to continue.
