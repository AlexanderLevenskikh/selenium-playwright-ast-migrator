# Project rules for agents

These rules are intended for OpenCode agents working in this repository.

## Scope discipline

- Prefer minimal, reviewable changes.
- Do not refactor unrelated code.
- Do not rename public APIs unless explicitly requested.
- Do not change generated files unless the task explicitly says so.
- Do not solve adjacent problems unless the user asks for them.
- If the requested task is ambiguous, make a reasonable narrow assumption and state it.

## Verification

- For C# changes: run the smallest relevant `dotnet test` or `dotnet build`.
- For TypeScript changes: run focused tests/lint/typecheck when available.
- For Playwright changes: prefer focused test runs before broad runs.
- If verification is skipped, explain why.
- Never claim completion if required verification was not run.

## Git safety

- Never commit.
- Never push.
- Never run destructive git commands without explicit user request.
- Never delete files without explicit user request.

## Reporting

Final report must include:
- changed files;
- verification result;
- remaining risks;
- anything intentionally not fixed.

## Code quality

- Prefer existing project patterns.
- Avoid broad rewrites.
- Avoid speculative abstractions.
- Add regression tests when fixing bugs if a suitable test location exists.
- Do not hide TODOs by suppressing diagnostics unless explicitly justified.

## Migrator-specific notes

Use these only for Selenium → Playwright migrator tasks:

- Keep migrations target-safe.
- Prefer semantic/Roslyn-based fixes over string hacks.
- Preserve compile-smoke expectations.
- New mappings should have regression tests when possible.
- Do not suppress unsupported actions just to reduce counters.
- Generated output should remain deterministic.
- If adapter config changes, explain why.
