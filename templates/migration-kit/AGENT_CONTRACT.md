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
