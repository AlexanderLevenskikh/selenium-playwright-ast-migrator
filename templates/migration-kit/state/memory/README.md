# Project-scoped migration memory

This directory is the inspectable memory for this migration workspace. It is not global AI memory and is not shared across repositories by default.

Agents must treat memory as guidance, not authority:

- read `memory-summary.md` before planning;
- run `selenium-pw-migrator memory explain --workspace migration` when context is unclear;
- record decisions, warnings, rejected approaches, and final-gate lessons after bounded actions;
- never use memory to suppress assertions or hide over-suppressed user interactions;
- keep POM uncertainty reviewable until target mappings exist.

- `recall-index.json` and `recall-ledger.jsonl` record scoped memory recall receipts used by final gate.
