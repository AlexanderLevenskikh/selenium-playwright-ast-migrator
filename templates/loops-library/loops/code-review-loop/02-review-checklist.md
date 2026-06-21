# Review Checklist

Use this checklist, but do not dump it into the final answer.

## Correctness

- Does the code do what the task says?
- Are edge cases handled?
- Are errors handled?
- Are async/concurrency cases safe?
- Is state updated consistently?
- Are null/undefined cases safe?

## Regression risk

- Could existing behavior change unexpectedly?
- Are public APIs changed?
- Are generated outputs/snapshots impacted?
- Are migrations backward compatible?

## Tests

- Are important paths covered?
- Are regression tests added for bug fixes?
- Are tests meaningful rather than just snapshot churn?
- Were tests weakened?

## Runtime

- Does app start?
- Does main route render?
- Any console/network errors?
- Any auth/bootstrap/env issues?

## Security

- Any unsafe input handling?
- Any secrets/logging risks?
- Any dependency/security regression?
- Any permission/auth change?

## Maintainability

- Is the change scoped?
- Is there unnecessary refactoring?
- Does the code follow local patterns?
- Is the complexity justified?

## Docs/config

- Are docs updated if behavior/API/commands changed?
- Are CI/build/config changes consistent?
