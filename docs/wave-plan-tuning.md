# Wave-plan tuning without agents

`migration tune-wave-plan` is a deterministic planning experiment. It scans the Selenium test inventory, evaluates many candidate batching profiles, and writes a ranked recommendation without invoking supervisor, executor, reviewer, watchdog, sentinel, or the migration pipeline.

```bash
selenium-pw-migrator migration tune-wave-plan \
  --input ./SeleniumTests \
  --workspace migration \
  --out migration/plan-tuning
```

`migration plan --wave-profile auto` runs the same experiment automatically before writing the production plan:

```bash
selenium-pw-migrator migration plan \
  --input ./SeleniumTests \
  --strategy wavefront \
  --workspace migration \
  --out migration/plan \
  --wave-profile auto
```

The experiment derives its search ranges from the inventory itself: test/file counts, action and complexity quantiles, and the distribution of tests across source files. It varies test capacity, source-file capacity, soft/hard action and complexity budgets, and the marginal cost of additional tests from an already-open source file. It ranks candidates using a static total-cost proxy:

- estimated migration work;
- number of complete wave role cycles;
- coordination risk from an excessively large wave;
- non-smoke singleton waves;
- source-file fragmentation across waves;
- soft-budget overruns;
- load imbalance;
- heavy single-test waves.

The first test from a source file pays full estimated complexity. Later tests from the same file pay only a configured marginal percentage because file discovery, fixtures, Page Objects, helper semantics, and many mappings are reusable. This is why additive per-test complexity caused pathological one-test waves.

Outputs:

- `wave-tuning.json` — machine-readable experiment and top candidates;
- `wave-tuning.md` — operator-facing recommendation;
- `recommended-preview/waves.json` and `plan.md` — preview generated with the recommended profile.

An adaptive reference wave count is derived for every inventory, but it is not forced on the optimizer. The profile with the lowest estimated total cost wins. A small inventory may produce 2–3 waves, a medium inventory may produce a single-digit or low-double-digit count, and a large inventory expands only as required by safe capacity. Passing `--target-waves` turns a desired count into an additional optimization constraint.

Soft budgets guide packing. Broad hard ceilings are derived from the current inventory quantiles, and only those ceilings produce `BLOCKED`. `PASS`, `SOFT_LIMIT_EXCEEDED`, and `HEAVY_SINGLE_TEST` remain executable states. `wave-tuning.md/json` also records cost components, the gap to the runner-up, and recommendation confidence.

Named profiles are also available:

- `auto` — recommended; runs the experiment for the current inventory;
- `compact` — fewer, larger waves;
- `balanced` — stable general-purpose batching;
- `conservative` — smaller waves;
- `manual` — honors explicit `--max-wave-*`, `--hard-wave-*`, and `--same-file-marginal-cost` values.

To calibrate the static model against observed agent runtime, pass `--role-overhead <weight>`. Increase it when role startup/review/final-gate overhead dominates; the tuner will prefer fewer waves. This does not claim exact wall-clock prediction, but it makes the trade-off explicit and reproducible.
