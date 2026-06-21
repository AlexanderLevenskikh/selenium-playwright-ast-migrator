# Self Review Checklist

Use this checklist internally.

## Original task alignment

- Did the change solve the requested task?
- Did scope creep happen?
- Are unrelated files changed?
- Are follow-up issues separated from the current fix?

## Correctness

- Are edge cases handled?
- Any broken async/control flow?
- Any incorrect state update?
- Any null/undefined/exception path missed?

## Tests

- Is there regression coverage?
- Were tests weakened?
- Are snapshots intentional?
- Are important paths untested?

## Runtime

- Could app startup/rendering/routing/auth be broken?
- Should runtime smoke be run?
- Any console/network errors?

## Security

- Any unsafe input/logging/secrets?
- Any dependency/audit regression?
- Any auth/permission risk?

## Maintainability

- Is the fix minimal?
- Does it follow existing patterns?
- Any unnecessary abstractions?
- Any readability issue severe enough to fix now?

## Docs/config

- Did commands/API/behavior change?
- Are docs/config/CI updated if needed?
