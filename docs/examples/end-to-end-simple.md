# End-to-end simple example

This example uses real files from `examples/simple/`:

```text
examples/simple/
  input/SimpleSeleniumTest.cs
  adapter-config.json
  expected/SimplePlaywright.generated.cs
  report.example.txt
```

It demonstrates the smallest useful migration loop: Selenium input → adapter config → generated Playwright .NET output → report review.

## 1. Inspect the Selenium input

`examples/simple/input/SimpleSeleniumTest.cs` contains two NUnit tests that use a `LoginPage` PageObject:

```csharp
page.Username.InputText("admin");
page.Password.InputText("secret123");
page.SubmitButton.Click();
```

## 2. Inspect the adapter config

`examples/simple/adapter-config.json` maps PageObject expressions to Playwright test ids:

```json
{
  "SourceExpression": "page.SubmitButton",
  "TargetExpression": "GetByTestId(\"submit-button\")",
  "TargetKind": "TestId"
}
```

In a real project, use selectors from source truth. Do not invent test ids from PageObject names.

## 3. Run the migration

From the repository root:

```bash
selenium-pw-migrator --mode migrate \
  --input examples/simple/input \
  --config examples/simple/adapter-config.json \
  --out examples-simple-generated \
  --format both
```

The output is written to `migration/examples-simple-generated` unless you override `--workspace` or use an absolute `--out` path.

## 4. Compare expected generated output

The expected output is checked into `examples/simple/expected/SimplePlaywright.generated.cs`.

Important generated lines:

```csharp
await Page.GetByTestId("username").FillAsync("admin");
await Page.GetByTestId("password").FillAsync("secret123");
await Page.GetByTestId("submit-button").ClickAsync();
```

The setup also includes an explicit TODO for unsupported navigation/loading code. That is intentional: the migrator preserves uncertainty instead of silently deleting behavior.

## 5. Next iteration

To improve this example, add method mappings for project-specific setup/helper calls or configure the generated `TestHost` for your real Playwright project. Then rerun `migrate` and `verify-project`.
