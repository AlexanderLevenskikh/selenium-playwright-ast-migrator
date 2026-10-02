# Agent Benchmark Protocol

Phase A, protocol only. Purpose: give the four agent architectures (A: agent from
scratch, B: deterministic-only, C: agent-with-handoff, D: built-in orchestration) a
**reproducible, comparable** score on the migration task — so a future vNext choice is a
measured claim, not a taste.

Scope: exercises the *generation* task on `corpus/stable/vertical-slice` only (no
browser runtime in this protocol; semantics is judged on generated **code**, then by the
`run` quality gates + `verify-project` where a corpus fixture supports it).

## 1. Task unit

One (root-cause, source fixture, target shape) triple. For each triple an agent gets:

```
source:  <fixture>/Tests/<File>.cs
config:  <fixture> adapter-config.json         # empty for the "unconfigured" arm
target:  empty Playwright skeleton (csproj + PageTest base)
goal:    produce target source file that (a) compiles against the Playwright harness,
         (b) passes fixture quality budget (todoMax/unmappedMax/unsupportedMax),
         (c) preserves every executable assertion/locator as ACTIVE code.
```

## 2. Arms

| Arm | Tooling | Notes |
|---|---|---|
| A — from scratch | agent + `selenium-pw-migrator` **disabled** (source→target by hand) | baseline ceiling/difficulty |
| B — deterministic only | `run` with adapter-config authored by agent | baseline floor |
| C — handoff | agent reads config/manifest, delegates generation to `run`, fixes residual by hand | the expected production shape |
| D — built-in orchestration | `supervised-task` loop | measures orchestration overhead/benefit |

## 3. Metrics per triple (all collected, none omitted)

- correctness: generated-code compile (0 errors), todo count, unmapped count, raw
  statement count, assert-preservation ratio (executableAssertionsPreserved / source),
  `verify-project` outcome when supported
- effort proxies: files read, files written, tool calls, wall-clock, LLM prompt tokens
  (only if the provider exposes them — otherwise recorded as `n/a`, never fabricated)
- determinism: same triple twice → generated diff (`0` = reproducible; document any diff)
- gate interaction: did the run-level quality gate catch a regression the agent missed?

## 4. Controlled variables

- fixed fixture order and fixed per-arm budget cap per triple
- identical environment; record .NET SDK version, tool version, toolchain
- the agent is told the *rules* (exact same instructions preamble) in all arms

## 5. Reporting

One table per triple, one summary per arm. Emitted under `artifacts/baseline/agent-eval/`.
Threshold: an arm's outcome counts as "pass" only if it meets (b) AND (c); (a) is a
necessary precondition.

## 6. Current status

- Protocol written in Phase A; **not yet executed** (no agent runs were measured in this
  session — the four arms need an agent harness that was intentionally out of the Phase A
  baseline scope).
- `corpus/planning/phase-a-coverage.md` is the initial fixture set (groups 1–14).
