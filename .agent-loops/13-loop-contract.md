# Loop Contract

This file defines the concrete loop shape agents must follow.

Use it as the top-level contract for migration runs. Other `.agent-loops/*`
files add details, but they must not weaken this contract.

## Default loop mode

The default mode is **migration-artifact loop**.

In this mode the agent works on migration inputs, generated output, config,
profiles, reports, migration board, helper inventory, POM evidence, and verify
artifacts.

The agent must not edit migrator repository source code unless the prompt
explicitly says:

```text
Mode: migrator-code
Repository source edits are allowed.
Allowed write paths include the migrator repository.
```

If migrator source changes seem necessary while in migration-artifact mode,
the agent must create a bounded finding or ticket candidate instead of fixing
the code.

## Required loop fields

Every loop must identify these fields before acting:

- `mode`: `migration-artifact`, `migrator-code`, `verifier`, or `coordinator`;
- `goal`: one concrete batch goal;
- `allowed input paths`;
- `allowed write paths`;
- `forbidden paths`;
- `max iterations`;
- `check command`;
- `exit condition`;
- `state file path`;
- `latest artifact/report path`.

If any required field is missing, infer it from the prompt or latest handoff
artifact. If it still cannot be inferred, stop with `BLOCKED_BY_MISSING_INPUT`
and name the missing field. Do not ask an open-ended question.

## Iteration shape

Each iteration follows the same shape:

1. Inspect the current artifact/report/log for the selected goal.
2. Choose one small reversible action.
3. Make only changes allowed by the current mode and path boundaries.
4. Run the check command or the strongest affordable local check.
5. Read the output.
6. Update state/handoff with what changed, command result, and next action.
7. Compare the result with the exit condition.
8. Continue unless the exit condition or stop policy is reached.

## Exit condition

The exit condition must be objective and command/report based.

Good examples:

- `project-verify-report.json has zero error diagnostics`;
- `migration-board.md no longer lists EMPTY_TEST_AFTER_SUPPRESSION`;
- `top normalized root cause count decreases or is classified with source evidence`;
- `config-validate passes with no dangerous suppression errors`;
- `one selected smoke candidate runs and failure is classified`.

Bad examples:

- `seems good`;
- `probably enough`;
- `agent made progress`;
- `build is green` when the board still has actionable migration work.

## Anti-gaming rules

Do not satisfy the check by weakening it.

- Do not delete tests to make checks pass.
- Do not hide TODOs with broad suppressions.
- Do not mark Selenium/source-only roots as target-known to silence diagnostics.
- Do not edit generated files manually as the final fix.
- Do not change the exit condition mid-loop to match current output.
- Do not broaden the task into migrator source repair unless mode is
  explicitly `migrator-code`.

## Stop behavior

If the exit condition is reached, report `READY_FOR_ACCEPTANCE` for the current
batch.

If the batch is complete but the migration board still contains actionable
categories, also report:

```text
Overall migration loop: CONTINUE_AUTONOMOUSLY
```

If blocked, produce a concrete blocked report with the exact missing input,
forbidden write, repeated failure, or product/business decision required.
Do not ask "continue?".
