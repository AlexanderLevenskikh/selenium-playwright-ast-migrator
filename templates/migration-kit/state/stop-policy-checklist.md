# Stop policy checklist

Stop and report only when at least one checked condition is true:

- [ ] `SUCCESS`: no meaningful unresolved migration work remains and required validation passed.
- [ ] `SOURCE_SCOPE_MISSING`: configured source or config is missing.
- [ ] `GLOBAL_TOOLING_BLOCKER`: required tooling prevents truthful work on every remaining candidate.
- [ ] `SCOPE_OR_EVIDENCE_VIOLATION`: protected paths or evidence integrity would be violated.
- [ ] `HUMAN_DECISION_REQUIRED`: every remaining useful candidate requires a concrete human product decision, missing source truth, or new write authorization.
- [ ] `STOPPED_TWO_CONSECUTIVE_NO_PROGRESS`: two consecutive completed cycles on distinct candidate fingerprints produced no progress, with no intervening progress.
- [ ] `AUTONOMOUS_CYCLE_BUDGET_REACHED`: five remediation cycles completed in ordinary, `continue`, or bounded mode. In `continuous` mode this is a checkpoint and automatic batch rollover, not a stop condition.

Additional rules:

- A successful cycle resets `noProgressStreak` to zero and autonomy continues while budget remains.
- The first no-progress cycle exhausts that candidate and triggers a different independent candidate; it is not a stop by itself.
- A known verify-project/CPM/transitive-build defect is tracked as a project-verification blocker, not a global stop, when other migration improvements remain independently measurable.
- At a budget stop in ordinary, `continue`, or bounded mode, rank remaining safe candidates and do not claim `COMPLETE` or “no automated work remains”. In `continuous` mode persist the checkpoint and automatically open the next five-cycle batch.
- `continue` opens a fresh five-cycle invocation budget but does not make exhausted candidates eligible without new evidence. `continuous` crosses budget checkpoints automatically and must not require the user to invoke `continue` between batches.
