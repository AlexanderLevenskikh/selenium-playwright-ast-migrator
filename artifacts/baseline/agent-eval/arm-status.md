# Agent Benchmark Protocol — arm status (Phase A.3, NEXT-D)

Scope: `corpus/stable/vertical-slice`. Protocol: `docs/vnext/agent-benchmark-protocol.md`.
Rule: no result is fabricated — unexecuted arms are `n/a` (protocol §3: "otherwise recorded
as `n/a`, never fabricated"). Machine-readable copy: `arm-status.json`.

## Summary

| Arm | Executed | Outcome | Evidence |
|---|---|---|---|
| A — from scratch | no | `n/a` — needs an external agent harness (tool disabled) | — |
| B — deterministic only | yes | 36/36 conforming; 0 regressions; 0 non-deterministic | `artifacts/lab/full-corpus-6` |
| C — handoff | no | `n/a` — protocol artifacts ready (agent-next-task.md × 36), delegated-repair triple run not measured | per-fixture `project-verify/agent-next-task.md` |
| D — supervised-loop | yes (pilot) | pilot data point: 5 Phase-A.3 commits, corpus runs 4/5/6 green, 1 gate-caught regression | `phase-a-findings.md`, `coverage-matrix.json` |

## 1. Arm B — the deterministic floor is measured

`run` (with agent-authored `adapter-config.json`) + `verify-project` over the whole
stable corpus is the arm-B (deterministic-only) outcome and is **fully measured**:

- `artifacts/lab/full-corpus-6`: **36/36 conforming** — 29 `PASS`, 5
  `UNSUPPORTED_AS_EXPECTED` (p26–p29, p33), p24b `INFRASTRUCTURE_FAILURE` (sabotage),
  p30 `SOURCE_INVALID` (broken source, fail-closed).
- Generated-code compile: 0 errors (every `PASS` fixture passes `verify-project` build
  + runtime quality gates `todoMax/unmappedMax/unsupportedMax`).
- Determinism: **0 non-deterministic** across full-corpus-4/5/6.

This is the floor any agent arm must beat, and the production path (deterministic engine
+ config authored from source) already clears it.

## 2. Arm C — handoff protocol is ready, execution not measured

Every lab fixture receives `project-verify/agent-next-task.md`: priority, category,
exact next task, top normalized root causes, commands, helper-inventory rule, acceptance
criteria, and do-not-do rules. That is the handoff *contract* the arm-C agent consumes.

What is **not** measured: a controlled arm-C triple run (agent reads config/manifest →
delegates generation to `run` → fixes residual by hand). That requires an external agent
harness that was intentionally out of the Phase A baseline scope (protocol §6), so no
arm-C outcome is declared here.

## 3. Arm D — supervised loop, pilot data point (this session)

This repository session operated as arm D (built-in orchestration / supervised-task
loop). Honest pilot evidence, not the full multi-triple benchmark:

- 5 sequential Phase-A.3 commits, each self-contained and regression-tested:
  `2d0dc56` (NEXT-B), `3d37277` (closeout), `232386c` (hygiene), `c702d7f` (G3),
  `aa48ffd` (NEXT-C + corpus-matrix fix).
- Full-corpus lab runs `full-corpus-4/5/6` each 100% conforming; 0 no-progress stops
  since the plan lock (NEXT-B → hygiene → G3 → NEXT-C → NEXT-D).
- Gate interaction measured once: the contract suite **caught a regression** that the
  pilot would otherwise have shipped — `c702d7f` added p34 without updating
  `coverage-matrix.json` / the 35-fixture contract counts; `LabScenarioContractTests`
  + `LabStableCorpusTests` failed and were fixed in `aa48ffd`. That is a real (n=1)
  example of the supervised gate catching what the loop missed.
- Tool-call / token counts: `n/a` (provider does not expose them) — never fabricated.

## 4. Threshold

Per protocol §5, an arm counts as "pass" only when it meets the quality budget (5b) AND
assert preservation (5c), with (5a) compile as precondition. Arm B meets it on all 29
runtime `PASS` fixtures. Arms A and C are not assessed ('not executed'). Arm D meets
5(a)/(b) on the same fixtures by delegation to arm B, and its own contribution
(orchestration/repair) is represented only by the pilot evidence above.

## 5. What a future real benchmark needs

- An external agent harness (LLM agent + tool access) to execute arms A and C per triple.
- Provider-exposed token counts (currently `n/a`).
- The fixed fixture order + per-arm budget caps defined in protocol §4.
