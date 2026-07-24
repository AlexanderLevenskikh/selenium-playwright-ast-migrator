---
description: Run or resume the standard Selenium-to-Playwright migration flow without partition planning.
agent: orchestrator
---

Use `$ARGUMENTS` as an optional bounded instruction. The normal entry point is simply:

```text
/supervised-task
```

## Core rule

There is one migration scope and one ordinary run directory. Do not create partition plans, partition workspaces, acceptance receipts, quality ledgers, or numbered partition state. Do not reconstruct missing validation evidence by hand.

Do not end a routine run with an opt-in question such as `Want me to continue?`. If the selected remediation is agent-executable, reversible, and permitted under `migration/**`, complete it as the single bounded improvement in the current invocation. Ask only when a human product decision or new write authorization is genuinely required.

## Start or resume

### Start-workspace no-menu fallback

When `current-ticket.md`, `start-dispatch.json`, or `next-commands.md` already identifies the migration, continue that migration directly. Do not offer options such as README updates, documentation cleanup, unrelated refactoring, or a generic task menu.

1. Resolve the repository root and keep all generated artifacts under `<repo-root>/migration/**`.
2. Read `migration/state/source-scope.json`, `migration/current-ticket.md`, `migration/next-commands.md`, `migration/state/memory/memory-summary.md`, and the active adapter config when they exist.
3. Inspect project-local guidance with `selenium-pw-migrator memory explain --workspace migration`. Treat memory as guidance, never as validation evidence.
4. Use the configured source from `source-scope.json` as authoritative. If it is absent or still a placeholder, stop with `SOURCE_SCOPE_MISSING`; do not guess a path and do not offer a broad setup menu.
5. Run install diagnostics and `kit doctor` before migration when the workspace was just installed or updated.
6. If no representative pilot exists, run `selenium-pw-migrator pilot` once. The pilot is calibration evidence only; it does not split the project into execution batches.
7. Run the complete source scope through the standard linear command:

```shell
selenium-pw-migrator run --input <selenium-source> --config <adapter-config> --out migration/runs/run-001 --format both
```

Use the next free `run-NNN` directory or reuse the current run only for a deliberate rerun after its generated output has been archived or cleaned safely.

8. When a real target project is available, run a fresh project verification for the same source and config:

```shell
selenium-pw-migrator verify-project --input <selenium-source> --config <adapter-config> --out migration/runs/run-001/verify-project --format both
```

Never write a synthetic PASS/NOT_RUNNABLE result. A missing SDK, missing target project, or CLI crash is a blocker to report, not evidence to manufacture.

9. Run the installed scope, policy, artifact, and final-gate checks against the same run. The final gate must fail when matching `verify-project` evidence is missing or non-passing:

```shell
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-scope.sh -RepoRoot . -AllowedRoots migration
./migration/scripts/validate-run-artifacts.sh -Workspace migration -RunPath migration/runs/run-001
./migration/scripts/check-final-gate.sh -Workspace migration -Run migration/runs/run-001 -RepoRoot .
```

Use the `.ps1` equivalents on Windows. Do not weaken or skip a failed gate.

10. Generate `explain-todo`, `smoke-plan`, and the static report dashboard from the concrete run artifacts when useful.
11. Review root causes by expected payoff. Choose the highest-payoff root cause that is supported by current evidence. Prefer one bounded config, generated-helper, or generated-POM improvement that removes a repeated pattern over editing many leaf TODOs.
12. After a change, rerun the same full standard flow and compare reports. Stop on a concrete blocker, repeated no-progress result, scope violation, or required human product decision.
13. Before final handoff, run `selenium-pw-migrator memory doctor --workspace migration`; report memory corruption or unsafe active guidance as a blocker.

## Continue

`/supervised-task continue` means: read the latest standard run, choose the highest-payoff root cause that is agent-executable and supported by current evidence, complete one bounded fix, and rerun the full project pipeline. It never means “start another partition”.

## Safety

- Source Selenium and product projects are read-only unless the user explicitly authorizes edits.
- Generated/proposed code stays under `migration/**` until reviewed.
- Do not reduce TODO counts by deleting actions, weakening assertions, adding suppressions, or inventing mappings.
- Do not claim runtime readiness without a fresh matching `verify-project` result and, where needed, a real smoke run.
- When the CLI crashes, preserve logs and report the crash. Do not bypass the failing gate by creating result JSON manually.

## Final report

Report the exact source, config, run directory, generated file/test/TODO totals, project-verification status, important blockers, changed files, and the single best next action. A standard run may finish with limitations; state them plainly.
