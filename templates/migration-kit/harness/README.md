# Standard migration lifecycle

The lifecycle is intentionally small:

1. doctor;
2. optional representative pilot;
3. one complete `selenium-pw-migrator run` in one active `migration/runs/run-NNN` directory;
4. matching `verify-project` when possible;
5. one bounded highest-payoff improvement;
6. rerun and compare.

Use the normal run artifacts and report dashboard as evidence. Stop on a concrete blocker or repeated no progress. Never reconstruct missing verification files manually.
