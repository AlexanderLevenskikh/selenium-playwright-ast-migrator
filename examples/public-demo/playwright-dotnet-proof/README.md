# Playwright proof for the public demo

This optional proof project opens the checked-in static app from `../app/index.html` with Playwright and executes the same user flow shown in the generated migration output:

1. sign in;
2. filter the catalog;
3. add the mug to the cart;
4. checkout;
5. assert the created order status.

It is intentionally separate from the default repository test suite: installing browser binaries can be slow on CI and is not required for normal migrator unit tests.

```bash
dotnet test examples/public-demo/playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj
```

If Playwright browsers are not installed yet, run the standard Playwright install command from that test project's build output first.
