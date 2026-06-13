# Examples

Sample inputs, configs, and outputs demonstrating Migrator usage.

## Simple example

`simple/` — minimal example with 1 test file and a basic adapter config.

- `input/` — sample Selenium test files
- `adapter-config.json` — basic config with UiTarget mappings
- `expected/` — expected generated output
- `report.example.txt` — example analyze report

## Profile examples

Real-world profile configs from pilot migrations:

- `widget-pilot/` — simple page test pilot
- `catalog-principals-pilot/` — table/list mappings, scope config
- `registry-pilot/` — complex page with method mappings
- `batch-migration/` — batch migration config for larger test sets

## Generated output examples

`output/` — samples of generated Playwright code:

- `batch-migration/` — generated files from a batch migration run

## How to use examples

1. Copy the `input/` files to your working directory
2. Copy and edit `adapter-config.json` with your project's selectors
3. Run:
   ```bash
   dotnet run --project Migrator.Cli -- --mode orchestrate --input "./input" --config "./adapter-config.json" --out "./output" --format both
   ```
4. Review the generated files and reports
