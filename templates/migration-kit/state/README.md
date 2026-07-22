# Standard migration state

This directory stores project-local evidence for the ordinary migration flow.

Source of truth:

```text
state/scope-contract.json
state/harness-policy.json
state/final-gate-result.json
state/current-ticket-status.json (optional bounded repair)
state/run-ledger.md
state/decision-log.md
state/memory/**
```

Each run lives under `runs/run-*`; only one run is active at a time, while older completed runs remain read-only history. The CLI creates run artifacts. Do not recreate missing reports or validation files by hand.

Use small repair batches: one root cause, one config cluster, or one engine bug with regression coverage. After a repair, repeat the complete configured source scope and compare current evidence.

Project-scoped memory is guidance, not authority. It cannot justify assertion suppression, empty tests, hidden interactions, or selectors without evidence.
