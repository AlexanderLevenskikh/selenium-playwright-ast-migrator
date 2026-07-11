# Validation-host scenario fixture

Small source/generated files used by `scripts/run-validation-host-smoke.ps1`.
The fixture intentionally has no external Selenium or Playwright packages: the planner reads source text, while validation-host syntax checks generated files and runs a tiny explicit process command.
