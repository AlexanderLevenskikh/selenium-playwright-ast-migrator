# Patch for .agent-loops/02-guardrails.md

Add this section.

## Checkpoint is not completion

Do not treat a green build/project verify as completion of the whole migration when the board still has actionable TODOs, missing mappings, unresolved symbols, unsupported actions, or blocked runtime candidates.

Treat green compile as a safe checkpoint.

Continue with the next highest-impact category unless the user explicitly asked only to fix compile/build errors.
