# Standard migration lifecycle

The lifecycle uses one configured source scope and bounded autonomous remediation cycles:

1. doctor;
2. optional representative pilot;
3. complete `selenium-pw-migrator run`;
4. matching `verify-project` when possible;
5. rank source-backed root causes;
6. execute one bounded change, review, full rerun, and evidence comparison per cycle;
7. continue after progress for up to five cycles per invocation;
8. after one no-progress cycle try a different candidate; stop after two consecutive distinct no-progress cycles or another mandatory stop.

Use real run artifacts and separate syntax, migration-quality, project-build, and runtime evidence. Never reconstruct missing verification files manually.
