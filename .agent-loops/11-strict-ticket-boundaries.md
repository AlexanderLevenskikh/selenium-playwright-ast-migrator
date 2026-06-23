# Strict Ticket Boundaries

These rules are mandatory when a task provides explicit input/output paths,
DLL/artifact folders, ticket folders, or a restricted workspace.

## Current ticket is the contract

The current ticket/task is the only active scope.

The agent must not broaden the task from:

- DLL/artifact analysis into source-code repair;
- report-only review into implementation;
- one ticket category into a general repository cleanup;
- verification into speculative architecture work.

If a newly discovered issue is outside the current ticket, record it as a finding
or next-ticket candidate. Do not fix it in the current run.

## Allowed paths

A task may define:

- allowed input paths;
- allowed write paths;
- forbidden paths.

When these are present, they override any generic instruction that would otherwise
encourage repository discovery.

Hard rules:

1. Read only allowed input paths and files required by explicitly allowed project commands.
2. Write only inside allowed write paths.
3. Do not traverse parent directories looking for source code, projects, solutions, configs, or artifacts.
4. Do not search the user's home/Desktop/work folders unless that exact path was allowed.
5. Do not open or edit `.cs`, `.csproj`, `.sln`, `.props`, `.targets`, `.json`, or generated files outside allowed paths.
6. If the task points to a DLL/artifact folder, treat that folder as the source of truth. Do not locate or edit matching source code.
7. If source code appears necessary, stop the current action and report why a source-change ticket is needed.

Before any write, perform a path-boundary check:

```text
Write target: <path>
Allowed write root: <path>
Reason: <which ticket requirement this satisfies>
```

If the write target is outside an allowed write root, do not write.

## Source edits

Source edits are forbidden unless the current ticket explicitly allows editing the
repository source tree.

Even when source edits are allowed:

- edit only files directly tied to the ticket;
- do not touch unrelated files discovered during investigation;
- do not hardcode project-specific generated-output fixes into generic migrator code;
- add/update regression tests for behavior changes;
- keep changes small and reversible.

## Do not ask “continue?”

Do not ask the user whether to continue after partial work.

Continue autonomously inside the current ticket until one of these stop conditions is reached:

- ticket completed and validated;
- required input path is missing or inaccessible;
- required write permission is missing;
- fixing the issue requires editing forbidden paths;
- source/product/business semantics are genuinely ambiguous;
- validation requires unavailable credentials/tools;
- maximum iterations/time budget was reached.

When blocked, produce a final blocked report with exact evidence and the next
single required human action. Do not ask an open-ended continuation question.

## Ticket adherence check

Every final report must include:

```text
Ticket adherence check:
- Current ticket followed: yes/no
- Allowed input paths inspected: <list>
- Writes performed only inside allowed write paths: yes/no/not applicable
- Source files opened outside allowed paths: yes/no
- Source files edited outside allowed paths: yes/no
- Unrelated issues fixed: yes/no
- Validation run: <commands or reason not run>
```
