# AST migration explained

Selenium-to-Playwright migration looks simple when the source code is small:
replace `Click()` with `ClickAsync()`, replace `InputText()` with `FillAsync()`,
and call it a day. Real suites are not that friendly. They usually contain
PageObjects, custom controls, helper methods, waits, assertion wrappers,
framework-specific setup, and selectors hidden behind several layers of code.

This is why Selenium Playwright Migrator treats migration as an AST and profile
problem, not as a text replacement problem.

Use the teaching demo while reading this article:

```bash
selenium-pw-migrator --mode analyze \
  --input examples/teaching-demo/input \
  --config examples/teaching-demo/adapter-config.json \
  --out teaching-demo-analyze \
  --format both

selenium-pw-migrator --mode migrate \
  --input examples/teaching-demo/input \
  --config examples/teaching-demo/adapter-config.json \
  --out teaching-demo-generated \
  --format both
```

## The tiny source test

The demo source test is intentionally ordinary:

```csharp
page.UserName.InputText("alex@example.com");
page.Password.InputText("correct horse battery staple");
page.SignInButton.Click();

page.DashboardTitle.ShouldBeVisible();
Assert.That(page.DashboardTitle.Text, Does.Contain("Dashboard"));
```

A text-based converter can spot words like `InputText`, `Click`, and `Assert`.
That is not enough. The useful migration question is more precise:

- What object is the action called on?
- Is the object a PageObject member, a local variable, or a helper return value?
- Which selector proves that `page.UserName` is the login email field?
- Is the assertion checking visibility, text, count, or something else?
- Can the target code be rendered safely, or should it stay as a TODO for review?

An AST-based converter can ask those questions because it sees code structure,
not just characters.

## Step 1: parse source code into structure

The C# parser turns source files into a syntax tree. For a line like this:

```csharp
page.UserName.InputText("alex@example.com");
```

Migrator can inspect the invocation as a structured node:

- receiver: `page.UserName`
- method: `InputText`
- argument: `"alex@example.com"`
- source line: the original line number and text

When semantic information is available, the migrator can go further and use type
information. When it is not available, it can still use syntax fallback and mark
confidence accordingly.

## Step 2: normalize intent into actions

The extractor converts recognized syntax into migration actions. In the teaching
demo, examples include:

| Source | Normalized action |
|---|---|
| `page.UserName.InputText("alex@example.com")` | fill text into `page.UserName` |
| `page.SignInButton.Click()` | click `page.SignInButton` |
| `page.DashboardTitle.ShouldBeVisible()` | visibility assertion on `page.DashboardTitle` |
| `Assert.That(page.PasswordError.Text, Does.Contain(...))` | text assertion on `page.PasswordError` |

This action model is the bridge between Selenium syntax and Playwright rendering.
It also makes reports useful: unmapped targets and unsupported helpers are grouped
by action, not buried inside generated code.

## Step 3: resolve source truth

The dangerous part of UI migration is selector guessing. The migrator should not
invent a locator just because a member is named `UserName`.

The teaching demo keeps source truth explicit in two places:

1. `examples/teaching-demo/input/PageObjects/LoginPage.cs` contains the original
   Selenium selector evidence:

   ```csharp
   public TextInput UserName => new(driver, By.CssSelector("[data-testid='login-email']"));
   ```

2. `examples/teaching-demo/adapter-config.json` maps source expressions and a few
   reviewed project helpers to target Playwright locators/statements:

   ```json
   {
     "SourceExpression": "page.UserName",
     "TargetExpression": "GetByTestId(\"login-email\")",
     "TargetKind": "TestId",
     "SourceTruth": "input/PageObjects/LoginPage.cs: UserName uses [data-testid='login-email']",
     "Confidence": "high"
   }
   ```

In a real project, you normally build this profile from several sources:
Selenium PageObjects, helper inventory, target Playwright PageObjects, reviewed
HTML attributes, and project-specific conventions.

## Step 4: render Playwright output

Once an action and a target locator are known, the renderer can produce code like:

```csharp
await Page.GetByTestId("login-email").FillAsync("alex@example.com");
await Page.GetByTestId("sign-in").ClickAsync();
await Expect(Page.GetByTestId("dashboard-title")).ToBeVisibleAsync();
```

The generated file should be readable and reviewable. It is allowed to contain
TODO comments when a source construct is not safely understood. That is a feature,
not an embarrassment: unresolved work is evidence for the next profile or
migrator improvement.

## Step 5: preserve or map uncertainty explicitly

The teaching demo setup contains project-specific behavior:

```csharp
page = Navigation.OpenLoginPage();
page.WaitUntilReady();
```

Those calls are not the same as ordinary UI actions. In the teaching demo, the
reviewed profile maps `Navigation.OpenLoginPage()` to `Page.GotoAsync("/login")`
because the route is known. The legacy `WaitUntilReady()` helper is preserved as
a source comment because its exact target wait policy is project-specific.

In a real project, similar setup code might belong in a Playwright fixture, a
navigation helper, a wait policy, or a project-specific PageObject constructor.
If the migrator cannot prove the right target behavior, it should preserve the
source as a TODO/source comment instead of fabricating working-looking code.

This is the core safety rule:

> Generated code can be incomplete. It must not be dishonest.

## Why profiles matter

A profile is not just configuration plumbing. It is the migration memory for a
project. Once the project proves that `page.UserName` maps to
`GetByTestId("login-email")`, every repeated usage can be migrated consistently.
Once a helper method is understood, the mapping can be reused across hundreds of
tests.

The practical workflow is:

1. Run `analyze` on a small pilot.
2. Inspect unmapped targets, unsupported actions, and TODO categories.
3. Use `index-pom`, `helper-inventory`, `discover-target`, or manual review to
   find source truth.
4. Update the profile.
5. Run `migrate` and `verify`.
6. Repeat until the remaining TODOs are intentional and reviewable.

## What the teaching demo proves

The teaching demo is small on purpose. It demonstrates the core promise of the
platform:

- Selenium code is parsed as structured code.
- UI actions are normalized into a reviewable intermediate model.
- Locators come from explicit source truth.
- Playwright code is rendered consistently.
- Uncertainty is preserved as TODO evidence.

That is the difference between a CLI converter and a migration platform.
