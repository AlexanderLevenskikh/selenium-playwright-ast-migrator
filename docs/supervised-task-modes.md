# `/supervised-task` modes

`/supervised-task` is an OpenCode command installed by `kit bootstrap-opencode`. The words after it are **arguments interpreted by the command prompt**, not nested `selenium-pw-migrator` CLI subcommands.

The installed source of truth is `.opencode/commands/supervised-task.md`; its template is `templates/opencode-team/global/.config/opencode/commands/supervised-task.md`.

## Base command reference

| Command | Aliases | Use it for | Expected behavior |
|---|---|---|---|
| `/supervised-task` | none | Resume safely from persisted state | Reads current ticket, continuation, final-gate, wave and memory state; chooses the next bounded action without asking for a broad menu. In default mode it stops at the next fresh successful checkpoint. |
| `/supervised-task <bounded request>` | free-form | Give the supervisor an explicit bounded request | Treats the text as the requested task while retaining state-aware dispatch, scope, review, risk, recovery and final-gate rules. It does not bypass an active blocker or current ticket. |
| `/supervised-task waves` | `/supervised-task wave`, `/supervised-task wavefront`, `/supervised-task start waves` | Start or bootstrap bounded wavefront migration | Resolves the configured source, updates/bootstrap the kit when needed, runs doctor, performs the no-agent auto-tuning experiment, plans affinity-aware waves, materializes the first pending wave and runs only its local scope. |
| `/supervised-task waves fresh` | `/supervised-task fresh waves`, `/supervised-task restart waves` | Archive an overgrown pilot and start a clean bounded wavefront | Runs `start-fresh-wavefront-run.*`, archives plan/runs/volatile state under `migration/archive/**`, preserves project memory and source scope, then replans from a one-test smoke wave. |
| `/supervised-task continue` | none | Resume after a persisted `FINAL_STOPPED_FOR_REVIEW` checkpoint | Runs the closed post-final loop: research, research review, task slicing, change review and at most one bounded executor task when approved. Plain `/supervised-task` also resumes this persisted state. |
| `/supervised-task continue <topic or task>` | none | Continue with a named research topic or concrete bounded task | Uses the text as the research topic or requested task, but still obeys current-ticket, review, scope, policy, risk and remediation budgets. |
| `/supervised-task sentinel` | `/supervised-task inspect`, `/supervised-task qa` | Run process/forensic inspection explicitly | Exports session evidence, invokes `harness-sentinel`, completes `sentinel-inspection.json`, and routes agent-executable findings into bounded follow-up tickets. This mode is intentionally one-shot. |

## Harness execution profiles

Use `--execution-profile` to choose how much Harness orchestration the current invocation should require:

| Profile | Command example | Behavior |
|---|---|---|
| `fast` | `/supervised-task waves --execution-profile fast` | **Default/lightweight mode.** Executor first; reviewer/watchdog/sentinel are added only by risk, policy, no-progress, or final-handoff requirements. |
| `standard` | `/supervised-task waves --execution-profile standard` | Balanced mode. Executor and reviewer are expected; watchdog/sentinel stay conditional until required. |
| `audit` | `/supervised-task waves --execution-profile audit` | **Full Harness mode.** Executor, reviewer, watchdog, and sentinel are all required. |

The modifier also works without `waves`, with `continue`, and with `continuous`:

```text
/supervised-task --execution-profile fast
/supervised-task continue --execution-profile standard
/supervised-task continuous --execution-profile fast
/supervised-task waves continuous --execution-profile audit
```

When the modifier is omitted, `fast` is selected for a new run. An existing wave keeps the immutable profile stored in its `execution-policy.json`. To change that profile, start a fresh wave/run; do not rewrite the policy in place.

All profiles still require scope enforcement, validation, final reviewer, final sentinel, and final gate. Profiles change orchestration cost, not safety guarantees.

## Continuous modifier

Add either `continuous` or `--continuation auto` to keep the current invocation running across checkpoints that would normally ask for another `/supervised-task continue` command.

The two forms are equivalent:

```text
/supervised-task continuous
/supervised-task --continuation auto

/supervised-task continue continuous
/supervised-task continue --continuation auto

/supervised-task waves continuous
/supervised-task waves --continuation auto

/supervised-task waves fresh continuous
/supervised-task waves fresh --continuation auto
```

The modifier is parsed independently from the base mode:

| Continuous command | Meaning |
|---|---|
| `/supervised-task continuous` | Ordinary state-aware resume, but do not pause at a fresh successful checkpoint while more runtime-approved work exists. |
| `/supervised-task <bounded request> continuous` | Execute the bounded request and continue through subsequent approved continuation states. |
| `/supervised-task continue continuous` | Resume the post-final loop and keep consuming approved bounded tickets/checkpoints. |
| `/supervised-task waves continuous` | Bootstrap/resume wavefront work and advance through eligible planned waves without requiring a manual `continue` between them. |
| `/supervised-task waves fresh continuous` | Archive/replan first, then run the new wavefront continuously. |

`continuous` is invocation-local. It does not permanently change execution profiles, policy, role budgets, remediation budgets, scope, review, sentinel, validation or final-gate requirements. It does not recursively invoke another slash command.

`sentinel`, `inspect` and `qa` remain one-shot forensic modes even when a continuation modifier is present.

## What continuous mode consumes

Continuous mode re-reads machine-readable state after every bounded action and continues through:

- `CONTINUE_REQUIRED`;
- `SAFE_CHECKPOINT`;
- a fresh successful `FINAL`/PASS checkpoint when more approved work remains;
- persisted `FINAL_STOPPED_FOR_REVIEW`;
- the next pending wave only after current-ticket, gate, sentinel, scope and wave-quality-budget state is clean.

A checkpoint is still written to evidence and is not renamed to `DONE`; it simply stops being a user-facing pause.

## Hard stop conditions

Every mode, including continuous mode, stops on:

- `DONE`;
- `FINAL_WITH_LIMITATIONS` or `WAVE_REMEDIATION_BUDGET_EXHAUSTED`;
- `HUMAN_DECISION_REQUIRED`;
- `BLOCKED` / `BLOCKED_*`;
- critical risk or confirmed scope violation;
- denied write permission;
- malformed, tampered or contradictory runtime/evidence;
- `NO_PROGRESS_DETECTED` after the allowed strategy-change budget;
- missing required user input;
- exhausted role, remediation, loop, time or other autonomous budget;
- explicit user stop.

Continuous mode means automatic continuation, not unlimited execution.

## Default stop semantics

Without the continuous modifier, a fresh successful `FINAL` checkpoint stops once for review and recommends `/supervised-task continue`. A later zero-argument invocation or `/supervised-task continue` resumes when state is `FINAL_STOPPED_FOR_REVIEW`.

`FINAL_WITH_LIMITATIONS` / `WAVE_REMEDIATION_BUDGET_EXHAUSTED` is always a hard autonomous stop. Use `/supervised-task waves fresh` to archive and restart, or explicitly authorize a remediation-budget change.

## Updating an existing workspace

After installing a newer Migrator build, update the guarded runtime scripts and OpenCode command pack before using new modes:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source <selenium-source> `
  --opencode-install none
```

`bootstrap-opencode` enters update mode when the workspace already exists. It backs up the old workspace, overwrites kit-owned runtime scripts **and the managed `migration/opencode-team/**` command pack**, refreshes guard checksums, and then reapplies `.opencode/agents` and `.opencode/commands` in the repository root. Managed OpenCode commands therefore update without `--force`; project-owned files such as the root `AGENTS.md` still keep their safer preservation rules.

## Validate the installed scripts, not only the source templates

Repository validation checks the Migrator source tree. To also check a generated/installed workspace copy:

```powershell
pwsh ./scripts/validate-scripts.ps1 -Root . -Workspace <path-to-product-repo>/migration -RequireShell
```

Inside the product repository, use the workspace-local validator:

```powershell
pwsh ./migration/scripts/validate-installed-scripts.ps1 -Workspace migration -RequireShell
```
