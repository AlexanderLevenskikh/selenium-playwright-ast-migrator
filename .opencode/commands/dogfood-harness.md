---
description: Run a small dogfood pass for the Migrator Agent Harness Kit.
agent: orchestrator
---

Task:
$ARGUMENTS

Run a bounded Harness Kit dogfood pass.

1. Read `docs/migrator-agent-harness-dogfood.md` first. It is the canonical dogfood reference.
2. Read `migration/state/harness-policy.json` when a migration workspace exists.
3. Create or resume an active run with `migration/scripts/new-harness-run.ps1` before doing implementation work.
4. Read the active run files before editing:
   - `migration/runs/<run-id>/Prompt.md`
   - `migration/runs/<run-id>/Plan.md`
   - `migration/runs/<run-id>/Implement.md`
   - `migration/runs/<run-id>/Documentation.md`
   - `migration/runs/<run-id>/trace.jsonl`
5. Keep the dogfood task tiny. Prefer docs/template/evidence-only changes.
6. Use dogfood allowed roots only when dogfooding inside the Migrator repository:
   - `migration/**`
   - `docs/**`
   - `templates/migration-kit/**`
   - `templates/opencode-team/**`
   - `scripts/**`
   - `Migrator.Tests/**`
7. Normal product migration runs remain artifact-only: write only under `migration/**`.
8. Write at least one event with `migration/scripts/write-harness-event.ps1` when the workspace exists.
9. Run `migration/scripts/check-harness-policy.ps1` and `migration/scripts/check-scope.ps1` with explicit allowed roots for the dogfood scope.
10. Do not ask routine continuation questions when the next action is allowed by `harness-policy.json` and OpenCode permissions.
11. Report `FINAL` only if the relevant scope/harness/final gates pass. Otherwise report `NOT FINAL - INVESTIGATION RESULT ONLY` and point to evidence.
