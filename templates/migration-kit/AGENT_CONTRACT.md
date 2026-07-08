# Agent Contract

This file is the short operational contract for every migration checkpoint.
Before each major action, restate which rule allows the action.

1. Allowed writes: `migration/**` only, unless the human gives a stricter workspace.
2. POM means generated/shadow/proposal POM under `migration/**`, not the real POM project.
3. Real target project, production POM, Playwright test project, `.csproj`, `nuget.config`, and root-level generated file edits are forbidden in artifact-only mode.
4. TODO reduction via suppression is failure, especially FluentAssertions/NUnit/business assertion suppression.
5. `0 TODO` is not success without scope-clean, quality-gate, and verification evidence.
6. If blocked, write a classified blocker/proposal artifact; do not ask vague continuation questions.
7. Runtime-ready/final claims require project verify evidence, or the report must say `NOT RUNTIME READY`.
8. The agent final answer is only another artifact until `state/final-gate.md` is PASS.
9. After a non-final final gate, read `state/continuation-decision.json`; if it says `CONTINUE_REQUIRED`, execute exactly one next bounded action before any user-facing handoff.

9a. Before planning or reviewing a supervised task, read `agent-skills/skill-map.md` and load only the relevant `SKILL.md` contracts. Skills guide behavior; they never override allowed writes, OpenCode permissions, scope guard, harness policy, or final gate.

10. After a successful FINAL/PASS checkpoint, stop and report. Do not start another run or ticket unless the user explicitly says continue or state/continuation-decision.json grants bounded auto-continuation for that exact next action.

11. Post-final research is not a terminal human handoff. `MANUAL_REVIEW` and `Developer action` mean “needs source-backed review and task slicing” until `migration-research-lead` and `migration-task-slicer` classify the work as non-agent-executable.
12. After reviewed post-final research, create bounded tickets before asking the user to manually continue: researcher → research lead → task slicer → supervisor/executor is the default closed loop when writes stay under `migration/**`.

## Project-scoped migration memory

Before planning a bounded action:

- Read `state/memory/memory-summary.md`.
- Read `plan/plan.md`, `plan/waves.json`, and `plan/memory-recall.md` when a divide-and-conquer wave plan exists.
- Resolve the repository root before wave planning/execution. `migration/**`, `migration/plan/**`, and `migration/runs/**` are repository-root artifacts, not source-project-relative artifacts. Do not run kit/plan/run-wave from `Web/**` or any source/target subdirectory. A nested workspace such as `Web/**/migration/**` is `NESTED_MIGRATION_WORKSPACE` and must be reported/repaired before implementation continues.
- If no wave run workspace exists for the selected wave, prepare it from the repository root with `selenium-pw-migrator migration run-wave --plan migration/plan --wave <wave-id> --workspace migration --out migration/runs/<wave-id>` before implementation.
- If files in scope are known, run `selenium-pw-migrator memory explain --workspace migration` and `selenium-pw-migrator memory recall --file <file> --workspace migration`, or inspect `state/memory/decisions.jsonl`, `warnings.jsonl`, `antipatterns.jsonl`, and `final-gate-lessons.jsonl`.
- Treat memory as guidance, not authority: apply a remembered rule only when its scope/conditions match the current evidence.

After implementation/review:

- Record durable decisions, warnings, rejected approaches, and final-gate lessons with `selenium-pw-migrator memory add ...` or `migration/scripts/write-memory-entry.ps1`/`.sh`; do not hand-write or overwrite JSONL memory files.
- Emit or update wave-local `config-delta.json` and `memory-delta.jsonl`; do not silently rewrite the global adapter config as a memory side effect.
- To combine reviewed wave deltas, run `selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge`, then `selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge`.
- Treat `migration/config-merge/adapter-config.merged.json` as a candidate only; do not replace the active adapter config until Reviewer, Watchdog, and Final Gate accept `merge-report.md`, `validate-merge-report.md`, and empty `conflicts.jsonl`.
- Keep `migration/runs/<wave-id>/input-scope.json`, `run-summary.md`, and `wave-status.json` as evidence for the bounded wave.
- Run `selenium-pw-migrator memory doctor --workspace migration` and ensure `config-delta-merge` final-gate check is clean before final-gate handoff.

Memory safety rules:

- Memory cannot justify assertion suppression.
- Memory cannot justify hiding over-suppressed user interactions.
- Selector knowledge must have evidence before it is reused.
- POM uncertainty stays reviewable until a target mapping exists.


13. Once the active run is persisted as `FINAL_STOPPED_FOR_REVIEW`, `/supervised-task` must resume the closed post-final loop even with zero arguments. It must not stop merely because research already exists, TODOs are marked manual, or the stop checklist names missing source truth. Existing research must be reviewed, revised if needed, sliced into tickets, reviewed, and one bounded migration-artifact executor task must run unless `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` or a concrete reviewer/policy blocker is written. Explicit `/supervised-task continue` remains supported but is not required for this persisted state.


## Permission and state-integrity rules

14. OpenCode permission denials are authoritative. If an edit/write tool is denied, do not retry the same write through `bash`, PowerShell, Python, `sed`, `tee`, shell redirection, or any other alternate tool. Stop with `BLOCKED_BY_OPENCODE_PERMISSION_DENIED` and report the denied path and intended change.
15. JSONL ledgers are controlled append-only state; treat them as append-only JSONL ledgers. Do not manually overwrite `state/harness-events.jsonl`, `runs/*/trace.jsonl`, `state/memory/*.jsonl`, or `state/backlog/*.jsonl` with ad-hoc shell writes. Use `write-harness-event` for events/traces, `selenium-pw-migrator memory add` or `write-memory-entry` for memory additions, and `repair-memory-jsonl` only for explicit JSONL repair with backup.
16. Machine-readable state must be consistent before handoff. If `task-slice-result` or reviewer/gate evidence says `BLOCKED_NO_AGENT_EXECUTABLE_TASKS`, `state/continuation-decision.json` must not remain `CONTINUE_REQUIRED`.



## Session export and sentinel rules

17. Each supervised run should produce a forensic session artifact at `runs/<run-id>/opencode-session-export.md`. Use `scripts/export-opencode-session.ps1`/`.sh`; if a native OpenCode transcript is unavailable, create a best-effort export and preserve observable session excerpts in `runs/<run-id>/session-observations.jsonl`. Do not invent transcript content.
18. `harness-sentinel` is the process tester. It scans session exports, trace/events, machine-readable state, prompts, and OpenCode config for process bugs such as `PERMISSION_BYPASS_ATTEMPT`, `APPEND_ONLY_VIOLATION`, `STATE_CONTRADICTION`, `PREMATURE_DONE`, `HUMAN_HANDOFF_WITHOUT_BLOCKER`, `FULL_MIGRATION_IN_WAVE_MODE`, and `STALE_ROOT_OPENCODE_CONFIG`.
19. Open high/critical sentinel findings that are agent-executable must be routed to `migration-task-slicer` as bounded process-hardening tasks before a final handoff. Sentinel recommendations are not vague advice to the user unless they are explicitly non-agent-executable.


Sentinel inspections must be finalized with `migration/scripts/complete-sentinel-inspection.ps1` or `.sh`; final gate treats a missing active-run `sentinel-inspection.json` as a process defect.
