# vNext / Phase A Baseline Documentation

Evidence-first baseline of the **current** Migrator, produced before any architecture
change. Read in this order:

| Doc | Content |
|---|---|
| [`phase-a-findings.md`](phase-a-findings.md) | the final Phase A report + decision gate (start here) |
| [`current-pipeline.md`](current-pipeline.md) | real pipeline reconstruction from code (source of truth vs `docs/architecture.md`) |
| [`transformation-rules.md`](transformation-rules.md) | human-readable inventory (machine version: `artifacts/baseline/transformation-rules.json`) |
| [`correctness-risk-areas.md`](correctness-risk-areas.md) | evidence-backed correctness findings with repro/classification/next-action |
| [`semantic-accounting.md`](semantic-accounting.md) | the semantic ledger and the two metric/accounting defects |
| [`determinism-baseline.md`](determinism-baseline.md) | byte-level reproducibility methodology + measured IDENTICAL results |
| [`complexity-inventory.md`](complexity-inventory.md) | component inventory and KEEP/SIMPLIFY/MOVE-OUT/REMOVE candidates |
| [`agent-benchmark-protocol.md`](agent-benchmark-protocol.md) | arms A/B/C/D protocol for future agent comparisons |

Harnesses: `scripts/baseline/measure-baseline.ps1` (+ `.sh`) and
`scripts/baseline/semantic-accounting.ps1` (+ `.sh`). Coverage plan:
`corpus/planning/phase-a-coverage.{md,json}`.
