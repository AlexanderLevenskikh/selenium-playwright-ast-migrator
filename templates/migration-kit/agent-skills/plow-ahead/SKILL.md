# Skill: plow-ahead

Purpose: keep a supervised migration run moving through ordinary ambiguity without noisy permission questions.

## Use when

- The user asked for the ordinary migration flow to proceed without routine permission questions.
- The next action is bounded, reversible, and inside `migration/**`.
- The active standard run and policy already permit the next bounded step.
- The question would only ask the user to choose a routine implementation detail.

## Behavior

Convert routine ambiguity into conservative assumptions and continue. Record the assumption where the run evidence lives.

Good plow-ahead assumptions:

- choose the smallest bounded remediation inside the current full-project run;
- prefer low-risk files first;
- reuse existing naming/style nearby;
- run focused verification before broad verification;
- write proposal artifacts instead of touching real project files;
- keep generated code under the current run/workspace.

## Stop instead when

- a permission is denied;
- the intended write is outside the allowed workspace;
- source/target project identity is genuinely ambiguous;
- secrets, production systems, publishing, destructive git operations, or network/package changes are required;
- two attempts failed with the same blocker and no new evidence exists;
- the user has to make a product decision that cannot be safely inferred.

## Required output

When you made assumptions, include a short recap:

```text
Assumptions made:
- Chose the repeated helper-mapping root cause because it affects the most tests with one bounded config change.
- Kept output under the active migration/runs/run-001 directory because product files are read-only.
```

Never use this skill to bypass OpenCode permissions, scope checks, project verification, or the final gate.
