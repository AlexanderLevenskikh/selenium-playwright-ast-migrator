# Config layering example

Run with both layers:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate --input ./OldTests --config examples/profiles/layering/infrastructure-base.adapter.json --config examples/profiles/layering/projects/discounts.adapter.json --out layering-migrate --format both
```

The project layer overrides `page.SaveButton` from `SaveButton` to `DiscountsSaveButton` and extends target-known types.
