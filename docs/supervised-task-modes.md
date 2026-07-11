# `/supervised-task` modes

`/supervised-task` is an OpenCode command installed by `kit bootstrap-opencode`. The words after it are **arguments interpreted by the command prompt**, not nested `selenium-pw-migrator` CLI subcommands.

The installed source of truth is `.opencode/commands/supervised-task.md`; its template is `templates/opencode-team/global/.config/opencode/commands/supervised-task.md`.

## Command reference

| Command | Aliases | Use it for | Expected behavior |
|---|---|---|---|
| `/supervised-task` | none | Resume safely from persisted state | Reads current ticket, continuation, final-gate, wave and memory state; chooses the next bounded action without asking for a broad menu. |
| `/supervised-task waves` | `/supervised-task wave`, `/supervised-task wavefront`, `/supervised-task start waves` | Start or bootstrap bounded wavefront migration | Resolves the configured source, updates/bootstrap the kit when needed, runs doctor, performs the no-agent auto-tuning experiment, writes `wave-tuning.md/json`, plans affinity-aware waves, materializes the first pending wave and runs only its local scope. |
| `/supervised-task waves fresh` | `/supervised-task fresh waves`, `/supervised-task restart waves` | Abandon an overgrown pilot and start a clean bounded wavefront | Runs `start-fresh-wavefront-run.*`, archives plan/runs/volatile state under `migration/archive/**`, preserves project memory and source scope, then replans from a one-test smoke wave. |
| `/supervised-task continue` | none | Resume after a persisted `FINAL_STOPPED_FOR_REVIEW` checkpoint | Runs the closed post-final loop: research, research review, task slicing, change review and at most one bounded executor task when approved. Plain `/supervised-task` also resumes this state. |
| `/supervised-task continue <topic or task>` | none | Continue with a named research topic or concrete bounded task | Uses the text as the research topic or requested task, but still obeys current-ticket, review, scope, policy and remediation budgets. |
| `/supervised-task sentinel` | `/supervised-task inspect`, `/supervised-task qa` | Run the process/forensic inspection explicitly | Exports session evidence, invokes `harness-sentinel`, completes `sentinel-inspection.json`, and routes agent-executable findings into bounded follow-up tickets. |
| `/supervised-task <bounded request>` | free-form | Give the supervisor an explicit bounded request | Treats the text as the requested task while retaining all state-aware dispatch, scope, review and final-gate rules. It does not bypass an active blocker or current ticket. |

## Important stop semantics

A fresh successful `FINAL` checkpoint stops once for review. A later zero-argument invocation or `/supervised-task continue` resumes when state is `FINAL_STOPPED_FOR_REVIEW`.

`FINAL_WITH_LIMITATIONS` / `WAVE_REMEDIATION_BUDGET_EXHAUSTED` is different: it is a hard autonomous stop. Use `/supervised-task waves fresh` to archive and restart, or explicitly authorize a remediation-budget change.

## Updating an existing workspace

After installing a newer Migrator build, update the guarded runtime scripts and OpenCode command pack before using new modes:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source <selenium-source> `
  --opencode-install none
```

`bootstrap-opencode` enters update mode when the workspace already exists. It backs up the old workspace, overwrites kit-owned runtime scripts, refreshes guard checksums and reapplies repository-root OpenCode commands.

## Validate the installed scripts, not only the source templates

Repository validation checks the Migrator source tree. To also check a generated/installed workspace copy:

```powershell
pwsh ./scripts/validate-scripts.ps1 -Root . -Workspace <path-to-product-repo>/migration -RequireShell
```

Inside the product repository, use the workspace-local validator:

```powershell
pwsh ./migration/scripts/validate-installed-scripts.ps1 -Workspace migration -RequireShell
```

This distinction matters after updates: a source template can be valid while an older `migration/scripts/*.ps1` copy is still stale or malformed. Final gate runs the installed PowerShell syntax check before artifact hygiene when the validator is available.
