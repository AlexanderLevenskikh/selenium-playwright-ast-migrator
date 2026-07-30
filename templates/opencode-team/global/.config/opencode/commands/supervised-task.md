---
description: Run or resume the autonomous standard Selenium-to-Playwright migration flow.
agent: orchestrator
---

Use `$ARGUMENTS` as an optional mode or bounded instruction. Supported forms:

```text
/supervised-task
/supervised-task continue
/supervised-task continuous
/supervised-task continue continuous
/supervised-task <bounded request>
```

`continuous` and `--continuation auto` are aliases. Their position in the argument list does not matter.

## Core rule

There is one configured migration scope and one ordinary run lineage. Do not create hidden source partitions, acceptance receipts, quality ledgers, or synthetic validation evidence.

Autonomy is bounded by **remediation cycles**, not by user turns:

- one remediation cycle = one source-backed bounded change, review, full rerun, and evidence comparison;
- an ordinary or `continue` invocation receives a fresh budget of up to **5 remediation cycles**;
- the initial baseline run does not consume a remediation cycle;
- `continue` resumes from the latest real evidence and opens a new 5-cycle invocation budget;
- `continuous` means automatically begin the next safe cycle after progress instead of handing routine work back to the user;
- one bounded change is allowed **per cycle**, not per invocation.

Do not end a routine run with an opt-in question such as `Want me to continue?`. Ask only when every remaining useful candidate requires a human product decision, missing source truth, or new write authorization.

Before writing `No further automated migration work remains`, list every remaining root-cause cluster with its count, representative evidence, and a concrete stop reason. That sentence is allowed only when no safe agent-executable candidate remains.

## Invocation state

Read and update `migration/state/autonomy-state.json` before the first cycle and after every completed cycle. At invocation start:

1. preserve previous run evidence and exhausted candidate fingerprints;
2. create a new `invocationId`;
3. reset `cyclesCompleted` and `noProgressStreak` to `0`;
4. set `cycleBudget` to `5` unless a smaller explicit bounded request applies;
5. never retry an exhausted no-progress candidate without new evidence.

Rewrite `migration/state/handoff.md` as a complete document; never append a second status, field, or section to the existing template.

Use `migration/scripts/update-autonomy-state.ps1` (or `.sh`) to start the invocation and record every cycle rather than editing counters ad hoc. The updater enforces budget, exhausted candidates, progress resets, and distinct no-progress fingerprints.

## Start or resume

### Start-workspace no-menu fallback

When `current-ticket.md`, `start-dispatch.json`, or `next-commands.md` already identifies the migration, continue it directly. Do not offer unrelated work or a generic task menu. Do not offer options such as README updates when the configured migration is already known.

1. Resolve the repository root and keep generated/proposed artifacts under `<repo-root>/migration/**`.
2. Read `migration/state/source-scope.json`, `migration/current-ticket.md`, `migration/next-commands.md`, `migration/state/autonomy-state.json`, `migration/state/memory/memory-summary.md`, and the active adapter config when they exist.
3. Inspect project-local guidance with `selenium-pw-migrator memory explain --workspace migration`. Memory is guidance, never validation evidence.
4. Use the configured source from `source-scope.json` as authoritative. If absent or still a placeholder, stop with `SOURCE_SCOPE_MISSING`; do not guess.
5. Run install diagnostics and `kit doctor` after installation or update.
6. If no representative pilot exists, run `selenium-pw-migrator pilot` once. The pilot is calibration only and does not split execution.
7. Run the complete source scope through the full standard flow using the next free `run-NNN` directory, or a clearly archived/clean deliberate rerun directory:

```shell
selenium-pw-migrator run --input <selenium-source> --config <adapter-config> --out migration/runs/run-001 --format both
```

8. Run fresh project verification for the same source/config when a real target project is available:

```shell
selenium-pw-migrator verify-project --input <selenium-source> --config <adapter-config> --out migration/runs/run-001/verify-project --format both
```

Never write a synthetic PASS. Never copy or manufacture a PASS/NOT_RUNNABLE result.

9. Run scope, policy, artifact, and final-gate checks against that same run:

```shell
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-scope.sh -RepoRoot . -AllowedRoots migration
./migration/scripts/validate-run-artifacts.sh -Workspace migration -RunPath migration/runs/run-001
./migration/scripts/check-final-gate.sh -Workspace migration -Run migration/runs/run-001 -RepoRoot .
```

Use `.ps1` equivalents on Windows. Preserve a failed gate honestly.

10. Generate `explain-todo`, `smoke-plan`, and the static dashboard when useful.
11. Rank remaining root-cause candidates by expected payoff, source confidence, reversibility, and independence from known blockers.
12. Execute remediation cycles until a mandatory stop condition is reached.
13. Run `selenium-pw-migrator memory doctor --workspace migration` and `migration/scripts/validate-handoff.ps1 -Workspace migration` before final handoff.

## Remediation-cycle algorithm

For each cycle:

1. Select the highest-payoff root cause among **non-exhausted** candidates that is agent-executable and supported by current source evidence.
2. Record a stable candidate fingerprint and baseline metrics before editing.
3. Delegate exactly one bounded change to `executor`; review it with `reviewer`.
4. Rerun the complete configured source and all available checks.
5. Compare at least TODO count, mapped/unmapped targets, unsupported actions, generated-file/test counts, syntax errors, project-verification status, and the relevant root-cause cluster count.
6. Classify the cycle:
   - `PROGRESS` when a meaningful migration metric improves, a blocker is resolved, or new source-backed evidence unlocks a concrete next action;
   - `NO_PROGRESS` when the accepted change and complete rerun produce no meaningful improvement and no new evidence;
   - `BLOCKED` only when that candidate cannot be measured or completed safely.
7. On `PROGRESS`, reset `noProgressStreak` to `0` and automatically start the next safe cycle while budget remains.
8. On the first `NO_PROGRESS`, set the streak to `1`, mark that candidate exhausted, and try a different independent candidate. Do not stop yet.
9. Stop with `STOPPED_TWO_CONSECUTIVE_NO_PROGRESS` only after two consecutive completed no-progress cycles on distinct candidate fingerprints with no intervening progress.
10. At five completed cycles, emit the `AUTONOMOUS_CYCLE_BUDGET_REACHED` checkpoint. In ordinary, `continue`, or bounded mode, stop and rank remaining safe candidates without claiming completion. In `continuous` mode, persist the checkpoint, open the next five-cycle batch automatically, and keep working without requiring `/supervised-task continue`.

## Independent validation dimensions

Track these separately:

- generated syntax;
- migration-quality metrics and TODO clusters;
- project restore/build verification;
- runtime/smoke verification.

A failure in one dimension does not automatically block safe work in another. For example, a known CPM or transitive-build harness defect may leave project verification `BLOCKED` while source-backed UI mappings can still be added and measured through generated diffs, TODO/unmapped deltas, and syntax checks.

Do not say generated code “compiles cleanly” unless fresh project verification actually passed. `0 C# syntax errors` proves only syntax validity.

## Continue and continuous

`/supervised-task continue` resumes the latest standard run, opens a fresh five-cycle invocation budget, and starts with the best non-exhausted candidate. It never means “start another source partition”.

`/supervised-task continuous` applies the same bounded cycle algorithm and automatically advances after every successful cycle. It must not stop merely because one cycle failed to improve or because one five-cycle batch ended. At each budget checkpoint it starts the next five-cycle batch automatically, without requiring `/supervised-task continue`, until the two-cycle no-progress rule, a real blocker, a human-only decision, or success is reached.

A bounded free-text request gets one requested remediation cycle by default. Adding `continuous` permits additional automatic cycles after the requested change when safe candidates remain.

## Mandatory stop conditions

Stop only for:

- success with no meaningful unresolved migration work;
- missing configured source/config or unavailable required tooling that prevents all truthful progress;
- evidence-integrity or scope violation;
- protected-path edit requiring authorization;
- a concrete human product decision that blocks every remaining useful candidate;
- two consecutive independent no-progress cycles;
- the five-cycle invocation budget in ordinary, `continue`, or bounded mode. In `continuous` mode this boundary is a checkpoint and automatic batch rollover, not a final stop.

A failed `verify-project` is not by itself a global stop when the failure is an isolated harness/infrastructure defect and independent migration improvements remain measurable.

## Safety

- Source Selenium and product projects are read-only unless explicitly authorized.
- Generated/proposed code stays under `migration/**` until reviewed.
- Do not reduce TODO counts by deleting actions, weakening assertions, broad suppressions, or invented mappings.
- Do not claim runtime readiness without fresh matching project verification and, where needed, a real smoke run.
- Preserve CLI crashes and failed validation logs; never manufacture replacement evidence.

## Final report

Report the exact source, config, run directory, generated file/test/TODO totals, all validation dimensions, invocation mode, cycle budget/usage, batch/checkpoint history, per-cycle deltas, no-progress streak, exhausted candidate fingerprints, blockers, changed files, ranked autonomous next actions, every concrete stop reason, and human decisions only where genuinely required.
