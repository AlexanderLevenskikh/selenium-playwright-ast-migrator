# Public preview flow

`public-preview-flow/v1`

This is the short public-facing route for trying the migrator without losing the safety properties that make large migrations reviewable.

## Safe-by-default story

The migrator is intentionally not a one-shot converter. The public preview flow is:

```text
install
  -> doctor install
  -> playground or product start
  -> pilot / wave
  -> verify and final gate
  -> current-ticket follow-up loop when blocked
  -> mapping research memory for noisy waves
  -> feedback-bundle/v1 when the user wants to share evidence
```

The important promise is **evidence before scale**:

- generated files are treated as drafts until `verify-project` and final gate evidence agree;
- wave mode is file-scoped and stops when `BLOCKED_BY_GATE`, `current-ticket.md`, high/critical sentinel findings, or `BLOCKED_BY_WAVE_QUALITY_BUDGET` are active;
- noisy waves are routed to `mapping-research-memory/v1` instead of generating more noisy files;
- `feedback-bundle/v1` excludes project source by default and gives the user a `manifest.json` to review before sharing.

## Five-minute disposable path

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

Use this when you only want to see the CLI, reports, and generated sample output.

## Product repository path

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
```

Then follow `migration/next-commands.md`. For OpenCode workspaces, `/supervised-task waves` starts the wavefront workflow and plain `/supervised-task` resumes the next bounded action.

For day-to-day operations, read the [Wave mode operator runbook](wave-mode-operator-runbook.md). It is the canonical public-preview operator guide for `BLOCKED_BY_GATE`, `current-ticket.md`, sentinel finding lifecycle, noisy waves, mapping research memory, and feedback bundle handoff.

## When the run is red

Do not start another wave just because code was generated. Use this rule:

| State | Next action |
|---|---|
| `BLOCKED_BY_GATE` | Run or follow `slice-gate-followups.ps1`; execute `migration/current-ticket.md`. |
| `current-ticket.md` exists | Finish, verify, mark `DONE`, or mark `BLOCKED` with a reason. |
| high/critical sentinel finding is open | Route it through the sentinel lifecycle or mark it non-agent-executable / accepted risk with evidence. |
| `BLOCKED_BY_WAVE_QUALITY_BUDGET` | Run `collect-mapping-research-memory.ps1` and improve mappings/config/recognizers before another wave. |
| `verify-project` failed | Review `project-verify-report.*` and `project-verify-harness.csproj`; generated code is not verified. |
| user wants to report a bad migration | Run `create-feedback-bundle.ps1` and share the safe zip after reviewing `manifest.json`. |

## Feedback loop for improving the migrator

The preferred user-to-author handoff is not a private repository dump. Ask for a feedback bundle:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

or:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

The bundle turns real project pain into safe, reusable evidence: TODO clusters, unresolved symbols, verify harness snapshots, wave quality budget, sentinel findings, and mapping research candidates. The maintainer can then create a minimal synthetic fixture, add a regression test, and improve the migrator without seeing the original proprietary suite.

## Release notes and public preview readiness checklist

Before publishing or demoing a preview build, verify:

- `scripts/verify-distribution-final.ps1` passes locally;
- `README.md` and `README.ru.md` show the install -> doctor -> playground/start -> wave -> feedback bundle path;
- `docs/public-preview-flow.md` and `docs/wave-mode-operator-runbook.md` are linked from `docs/README.md`;
- `feedback-bundle/v1`, `mapping-research-memory/v1`, `verify-project-harness/v1`, and `artifact-hygiene/v1` are documented;
- the release notes do not claim full automatic conversion; they claim measurable, reviewable migration with safe follow-up loops.
