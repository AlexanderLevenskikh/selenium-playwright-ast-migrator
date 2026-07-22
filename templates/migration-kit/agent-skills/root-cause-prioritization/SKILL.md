# Root-cause prioritization

Treat repeated TODOs as a dependency graph rather than a flat count.

1. Identify root blockers: missing setup/page objects, unresolved symbols, reusable helpers, wait/assertion mappings, and recognizer gaps.
2. Separate downstream cascade TODOs from their root.
3. Choose one bounded root pattern with the highest affected-test count and reusable value. In a product workspace, prefer adapter-config/generated-helper/generated-POM changes; report recognizer/renderer defects with a minimal reproduction unless Migrator repository edits were explicitly authorized.
4. Require a before/after delta after regeneration of the same run.
5. Do not count comment deletion or suppression as progress.

A successful remediation restores executable declarations, actions, or assertions and reduces root uncertainty. A lower TODO count alone is not success.
