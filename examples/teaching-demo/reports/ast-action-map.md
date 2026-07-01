# Teaching demo AST action map

This file is intentionally hand-readable. It shows the mental model behind the
migration: parse source code into actions, resolve each action against reviewed
source truth, then render Playwright code. The tool should never invent selectors
when the mapping is missing.

| Selenium source | AST action | SourceExpression | Source truth | Playwright output |
|---|---|---|---|---|
| `page.UserName.InputText("alex@example.com")` | `SendKeysAction` / fill text | `page.UserName` | `LoginPage.UserName => [data-testid='login-email']` | `Page.GetByTestId("login-email").FillAsync("alex@example.com")` |
| `page.Password.InputText("correct horse battery staple")` | `SendKeysAction` / fill text | `page.Password` | `LoginPage.Password => [data-testid='login-password']` | `Page.GetByTestId("login-password").FillAsync(...)` |
| `page.SignInButton.Click()` | `ClickAction` | `page.SignInButton` | `LoginPage.SignInButton => [data-testid='sign-in']` | `Page.GetByTestId("sign-in").ClickAsync()` |
| `page.DashboardTitle.ShouldBeVisible()` | `VisibilityAssertionAction` | `page.DashboardTitle` | `LoginPage.DashboardTitle => [data-testid='dashboard-title']` | `Expect(Page.GetByTestId("dashboard-title")).ToBeVisibleAsync()` |
| `Assert.That(page.PasswordError.Text, Does.Contain("Password is required"))` | text assertion | `page.PasswordError` | `LoginPage.PasswordError => [data-testid='password-error']` | `Expect(Page.GetByTestId("password-error")).ToContainTextAsync(...)` |
| `page.WaitUntilReady()` | suppressed setup/helper | method call | reviewed as project-specific loader wait | TODO/source comment; move to fixture or explicit wait policy |

## What this demo teaches

1. **AST recognition keeps intent.** The migrator sees a method invocation on
   `page.UserName`, not just a string containing `InputText`.
2. **Source truth is explicit.** Locators come from `adapter-config.json` and the
   Selenium PageObject evidence, not from guessing.
3. **TODO comments are evidence.** Unsupported setup and project-specific waits
   are preserved so a reviewer can move them into the right Playwright fixture.
4. **Profiles are reusable.** Once `page.UserName` and friends are mapped, every
   repeated action can be rendered consistently across the suite.
