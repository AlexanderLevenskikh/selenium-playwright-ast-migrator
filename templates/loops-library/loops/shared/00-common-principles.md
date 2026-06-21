# Common Principles

These rules apply to all engineering loops.

## Autonomy

The agent is expected to make ordinary engineering decisions.

Do not ask the user to choose between implementation options.

Ask the user only when:

- product/business behavior is ambiguous;
- required private context is missing;
- the next step is destructive;
- manual QA or risk acceptance is required;
- the stop policy explicitly requires human input.

## Source of truth

Trust:

- repository code;
- tests;
- command output;
- CI config;
- package/build scripts;
- runtime logs;
- browser console/network output;
- issue/ticket text;
- git diff.

Do not trust previous agent claims without verification.

## Safety

- Do not weaken tests to pass checks.
- Do not broaden scope without reason.
- Do not perform unrelated refactoring.
- Do not hide failures.
- Do not delete code unless the task clearly requires it.
- Do not make destructive repository operations without explicit permission.

## Statuses

Use explicit statuses:

- `CONTINUE_AUTONOMOUSLY`
- `READY_FOR_ACCEPTANCE`
- `NEEDS_HUMAN_REVIEW`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`
- `BLOCKED_BY_MISSING_INPUT`
- `MAX_ITERATIONS_REACHED`

If status is `CONTINUE_AUTONOMOUSLY`, continue without asking the user.
