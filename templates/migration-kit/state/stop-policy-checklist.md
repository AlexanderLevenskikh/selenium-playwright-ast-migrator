# Stop policy checklist

Stop and report only when at least one checked condition is true:

- [ ] `SUCCESS`: no meaningful unresolved migration work remains and required validation passed.
- [ ] `SOURCE_SCOPE_MISSING`: configured source or config is missing.
- [ ] `GLOBAL_TOOLING_BLOCKER`: required tooling prevents truthful work on every remaining candidate.
- [ ] `SCOPE_OR_EVIDENCE_VIOLATION`: protected paths or evidence integrity would be violated.
- [ ] `HUMAN_DECISION_REQUIRED`: every remaining useful candidate requires a concrete human product decision, missing source truth, or new write authorization.
- [ ] `REMEDIATION_RESIDUAL_CANDIDATES_EXHAUSTED`: every current progress-bearing residual identity that Core exposed as an autonomous candidate has been tried without deterministic progress.
- [ ] `AUTONOMOUS_CYCLE_BUDGET_REACHED`: five remediation cycles completed in ordinary, `continue`, or bounded mode. In `continuous` mode this is a checkpoint and automatic batch rollover, not a stop condition.

Additional rules:

- `noProgressStreak` is telemetry only. It never proves a global plateau and never stops the run by itself.
- `REJECT_NO_PROGRESS` exhausts only the exact Core residual identity or identities passed with `--residual-id`.
- `REJECT_REGRESSION` requires rollback but does not exhaust the residual candidate; a bad implementation attempt is not proof that the candidate itself is impossible.
- A candidate label is descriptive only. Canonical exhaustion identity comes from stable residual IDs emitted by `selenium-pw-migrator remediation residuals`.
- A known verify-project/CPM/transitive-build defect is tracked as a project-verification blocker, not a global stop, when other migration improvements remain independently measurable.
- At a budget stop in ordinary, `continue`, or bounded mode, rank remaining safe residual candidates and do not claim `COMPLETE` or “no automated work remains”. In `continuous` mode persist the checkpoint and automatically open the next five-cycle batch.
- `continue` opens a fresh five-cycle invocation budget but preserves residual exhaustion, state hashes, and pending transaction proof. `continuous` crosses budget checkpoints automatically.
