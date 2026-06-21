# Combined Workflows

These are recommended sequences.

## Fix ticket, then self-review

```text
ticket-fix-loop
→ self-review-loop
→ human acceptance
```

## Review MR, then fix comments, then self-review

```text
code-review-loop
→ ticket-fix-loop or manual fix
→ self-review-loop
→ human acceptance
```

## Dependency update, then self-review

```text
dependency-security-loop
→ runtime smoke
→ self-review-loop
→ human acceptance
```

## Migrator autopilot, then self-review

```text
migrator-autopilot-loop
→ verifier loop
→ self-review-loop
→ human acceptance
```

## Rule of thumb

Use self-review as the last gate before you personally review the result.
