# Selenium to Playwright migration workspace

This workspace supports one standard full-project source scope with bounded autonomous remediation.

```shell
selenium-pw-migrator doctor install
selenium-pw-migrator pilot --input <selenium-source> --out migration/pilot
selenium-pw-migrator run --input <selenium-source> --config migration/profiles/adapter-config.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input <selenium-source> --config migration/profiles/adapter-config.json --run-manifest migration/runs/run-001/run-manifest.json --out migration/runs/run-001/verify-project --format both
```

The pilot is optional calibration and does not partition execution. `/supervised-task`, `/supervised-task continue`, and `/supervised-task continuous` use up to five one-change remediation cycles per invocation. Progress advances automatically; one no-progress cycle selects a different candidate; two consecutive distinct no-progress cycles stop the invocation.

Generated code and reports stay under `migration/**`; source and product projects remain read-only unless explicitly authorized. Missing SDK/project context or a CLI crash is recorded honestly, never replaced with hand-written validation evidence.
