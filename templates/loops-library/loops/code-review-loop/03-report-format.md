# Code Review Report Format

```md
## Verdict

<APPROVE_WITH_CONFIDENCE | APPROVE_WITH_MINOR_NOTES | REQUEST_CHANGES | NEEDS_HUMAN_REVIEW | BLOCKED_BY_ENVIRONMENT>

## Summary

Briefly describe what changed and the overall risk.

## Checks

- Build:
- Tests:
- Typecheck/compile:
- Lint:
- Runtime smoke:
- Not run:

## Findings

### BLOCKER

- ...

### MAJOR

- ...

### MINOR

- ...

### QUESTIONS

- ...

## Recommended next step

- ...
```

If there are no findings in a category, omit that category.
