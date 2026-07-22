# Selenium to Playwright migration workspace

This workspace supports one standard full-project migration flow.

```shell
selenium-pw-migrator doctor install
selenium-pw-migrator pilot --input <selenium-source> --out migration/pilot
selenium-pw-migrator run --input <selenium-source> --config migration/profiles/adapter-config.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input <selenium-source> --config migration/profiles/adapter-config.json --out migration/runs/run-001/verify-project --format both
```

The pilot is optional calibration. It does not partition execution. Use `/supervised-task` in OpenCode to run or resume this same linear flow.

Generated code and reports stay under `migration/**`; source and product projects remain read-only unless explicitly authorized. Missing SDK/project context or a CLI crash is recorded as a blocker, never replaced with hand-written validation evidence.
