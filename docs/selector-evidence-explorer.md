# Selector Evidence Explorer

`selector-evidence` is a read-only provenance report for selectors and locators. It answers the review question: **where did this generated Playwright locator come from, and can we prove it?**

```bash
selenium-pw-migrator selector evidence \
  --input migration/runs/latest \
  --config ./adapter-config.json \
  --out selector-evidence \
  --format both
```

Mode-compatible form:

```bash
selenium-pw-migrator --mode selector-evidence \
  --input ./OldTests \
  --config ./adapter-config.json \
  --out selector-evidence \
  --format both
```

## Outputs

- `selector-evidence.md`
- `selector-evidence.json`

The report groups evidence into chains:

```text
Selenium POM/source selector -> adapter-config UiTarget mapping -> generated Playwright locator
```

Each chain includes:

- source expression;
- source selector kind/value;
- source evidence file/line;
- config target kind/expression/scope;
- generated locator file/line;
- confidence score;
- unsafe/inferred flags;
- recommended next action.

## Confidence policy

- `high`: source evidence, config mapping, and generated locator agree.
- `medium`: evidence is useful, but one link in the chain is missing or weaker.
- `low`: partial evidence exists, but review is still required.
- `cannot-prove`: generated/config selector exists without matching Selenium POM/source truth.

`cannot-prove` entries must not become broad profile mappings until source truth is collected.

## Unsafe/inferred selectors

The explorer marks selectors as review-required when they use risky patterns such as:

- raw Playwright locator expressions;
- `Nth` matching/index-based locators;
- generated TODO locators;
- dynamic/interpolated selector values;
- mappings without source selector evidence.

## Recommended workflow

1. Run `index-pom` and `helper-inventory` when POM/helper evidence is unclear.
2. Run `migrate`/`verify` to generate locator output.
3. Run `selector-evidence` against the run directory and config.
4. Use high/medium confidence chains in PR/evidence packs.
5. Open focused tickets for `cannot-prove` and unsafe selectors.

The command never edits source tests, generated files, or config.
