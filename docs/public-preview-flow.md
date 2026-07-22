# Public preview flow

1. Install or update the CLI/standalone bundle.
2. Bootstrap the migration kit with the real Selenium source path.
3. Run `kit doctor`.
4. Optionally calibrate mappings on a small representative pilot.
5. Run one complete `selenium-pw-migrator run` over the configured source scope.
6. Run `verify-project` against the generated project.
7. Review the dashboard, TODO root causes, unsupported actions, artifact hygiene, and final gate.
8. Fix at most one highest-payoff root cause, then repeat the complete run.

A pilot is calibration, not final coverage. A missing project-verification report is not a pass. Feedback bundles contain reports, mapping memory, TODO explanations, and verification evidence but exclude private source by default.

See [standard migration flow](standard-migration-flow.md) for day-to-day operation.
