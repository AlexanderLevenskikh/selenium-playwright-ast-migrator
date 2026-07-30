# Standard migration flow

The supported workflow uses one configured source scope and one ordinary run lineage. It does not partition the source. Autonomy is implemented as bounded remediation cycles over the complete source.

## First run

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator run --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001/verify-project --format both
```

The pilot is optional calibration. It never replaces the complete run.

## Validation

Run installed checks against the same concrete run:

```shell
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-scope.sh -RepoRoot . -AllowedRoots migration
./migration/scripts/validate-run-artifacts.sh -RunPath migration/runs/run-001
./migration/scripts/check-final-gate.sh -Workspace migration -Run migration/runs/run-001 -RepoRoot .
```

Use `.ps1` equivalents on Windows. Preserve missing or failed project verification honestly.

## Autonomous remediation

An ordinary, `continue`, or `continuous` invocation receives up to five remediation cycles. The baseline run does not consume a cycle.

Each cycle:

1. chooses one non-exhausted, source-backed root cause;
2. records a stable candidate fingerprint and baseline metrics;
3. makes one bounded adapter-config, generated-helper, generated-POM, or other permitted change;
4. reviews the change;
5. reruns the complete configured source and all available checks;
6. classifies the result as `PROGRESS`, `NO_PROGRESS`, or `BLOCKED`.

Progress resets the no-progress streak and starts the next safe cycle automatically. The first no-progress cycle exhausts that candidate and tries a different independent candidate. Stop after two consecutive distinct no-progress cycles, a real blocker, a human-only decision, or five completed cycles.

`/supervised-task continue` starts a fresh five-cycle invocation budget while retaining run evidence and exhausted candidate fingerprints. `/supervised-task continuous` advances automatically between cycles and across five-cycle checkpoints without another user command.

## Validation dimensions

Keep generated syntax, migration metrics, project restore/build verification, and runtime/smoke verification separate. A known CPM or transitive-build verification-harness defect may leave project verification blocked without preventing source-backed mappings or helper improvements that remain measurable through diffs, TODO/unmapped deltas, and syntax checks.

Zero C# syntax errors does not prove that the project compiles.

## Handoff

Rewrite `state/handoff.md` completely, keep one status and one stop reason, synchronize it with `state/autonomy-state.json`, then run `scripts/validate-handoff.ps1` or `.sh`. At a cycle-budget boundary, report `AUTONOMOUS_CYCLE_BUDGET_REACHED`, rank remaining safe candidates, and do not claim completion.

## Upgrading an old workspace

Run kit update, keep a backup, and let the new kit add `state/autonomy-state.json` and the handoff validator. Existing real run evidence remains usable; do not reconstruct or copy validation evidence.

> In `continuous` mode, the five-cycle limit is a checkpoint rather than a stop: the agent automatically starts the next five-cycle batch without requiring another `continue` command and works until a real terminal condition.
