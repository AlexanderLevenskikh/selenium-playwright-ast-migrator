# Before/after migration report example

This report is a compact example of what a reviewer should look for after a first pilot run.

## Input

- Source file: `examples/public-launch-demo/selenium-tests/LoginSmokeTest.cs`
- Source framework: Selenium C# / NUnit
- Target backend: Playwright .NET
- Adapter config: `examples/public-launch-demo/adapter-config.json`

## Before: Selenium intent

```csharp
page.Username.InputText("admin@example.test");
page.Password.InputText("correct-horse-battery-staple");
page.SubmitButton.Click();
page.Toast.ShouldBeVisible();
```

## After: generated Playwright intent

```csharp
await Page.GetByTestId("login-username").FillAsync("admin@example.test");
await Page.GetByTestId("login-password").FillAsync("correct-horse-battery-staple");
await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
await Expect(Page.GetByTestId("login-success-toast")).ToBeVisibleAsync();
```

## Migration summary

| Metric | Value |
|---|---:|
| Source files | 1 |
| Tests | 1 |
| Mapped UI targets | 4 |
| Unmapped UI targets | 0 |
| Unsupported setup actions | 1 |
| Manual TODO categories | navigation/setup mapping |

## Review notes

- The field interactions were mapped from adapter-config entries, so no selector was invented.
- The remaining TODO is setup/navigation related: `Navigation.OpenLoginPage()` and `page.Loader.ValidateLoading()` need project-specific semantics.
- The next safe action is to add a navigation/setup mapping or target PageObject fixture, then regenerate.
- This is a successful preview migration because the uncertainty is visible and actionable.
