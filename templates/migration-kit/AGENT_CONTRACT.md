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

10. After a successful FINAL/PASS checkpoint, stop and report. Do not start another run or ticket unless the user explicitly says continue or state/continuation-decision.json grants bounded auto-continuation for that exact next action.

## Project-scoped migration memory

Before planning a bounded action:

- Read `state/memory/memory-summary.md`.
- Read `plan/plan.md`, `plan/waves.json`, and `plan/memory-recall.md` when a divide-and-conquer wave plan exists.
- If files in scope are known, run `selenium-pw-migrator memory explain --workspace migration` and `selenium-pw-migrator memory recall --file <file> --workspace migration`, or inspect `state/memory/decisions.jsonl`, `warnings.jsonl`, `antipatterns.jsonl`, and `final-gate-lessons.jsonl`.
- Treat memory as guidance, not authority: apply a remembered rule only when its scope/conditions match the current evidence.

After implementation/review:

- Record durable decisions, warnings, rejected approaches, and final-gate lessons with `selenium-pw-migrator memory add ...` or by writing JSONL under `state/memory/**`.
- Emit config deltas under `state/memory/config-deltas/`; do not silently rewrite the global adapter config as a memory side effect.
- Run `selenium-pw-migrator memory doctor --workspace migration` before final-gate handoff.

Memory safety rules:

- Memory cannot justify assertion suppression.
- Memory cannot justify hiding over-suppressed user interactions.
- Selector knowledge must have evidence before it is reused.
- POM uncertainty stays reviewable until a target mapping exists.

