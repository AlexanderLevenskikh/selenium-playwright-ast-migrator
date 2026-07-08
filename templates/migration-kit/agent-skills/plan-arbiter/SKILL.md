# Skill: plan-arbiter

Purpose: compare competing plans and choose one executable direction.

## Use when

- Two agents produced different plans.
- There are multiple plausible migration strategies.
- A plan affects many files, dependencies, framework versions, or harness policy.
- The user asks which direction is safer.

## Compare

Normalize each plan into:

- goal;
- assumptions;
- files/workspace touched;
- required permissions;
- source truth/docs used;
- risk/blast radius;
- verification gates;
- rollback or containment;
- expected output artifacts.

## Decision outputs

Choose one:

- `ADOPT_PLAN_A`
- `ADOPT_PLAN_B`
- `HYBRID_PLAN`
- `REVISE_FIRST`
- `BLOCKED`

Then write:

- the winning executable sequence;
- rejected assumptions and why;
- required watchdog/reviewer/final-gate checks;
- the first bounded executor task.

Do not average plans blindly. If neither plan has source evidence or a safe write boundary, choose `REVISE_FIRST` or `BLOCKED`.
