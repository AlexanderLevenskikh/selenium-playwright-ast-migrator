# Review → Fix → Verify Loop

This is the loop the user described:

```text
review code
→ fix serious comments
→ verify
→ review again
→ stop
```

## Safe version

Do not make it infinite.

Use:

```text
max cycles: 2
fix only BLOCKER/MAJOR automatically
report MINOR/QUESTION
```

## Suggested prompt

```text
Read shared loop files.
Start Review → Fix → Verify Loop.

First perform code review.
Then fix BLOCKER and MAJOR findings that are local and safe.
Run verification.
Perform one final review.
Stop when no BLOCKER/MAJOR findings remain or max 2 cycles is reached.
```
