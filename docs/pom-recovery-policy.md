# POM recovery policy

Broad POM suppressions are allowed only as a migration safety mechanism, not as the final desired state.

When the migrator encounters source-only PageObject expressions such as:

```csharp
page.MenuItems.Error
page.Table.Rows.First()
lightbox.OkButton.Click()
modal.Close()
dialog.Confirm()
popup.WaitLoaded()
```

the agent must not immediately suppress them only to reduce TODO count.

Instead, the agent should run a POM recovery pass.

## Goal

Try to recover source truth from Selenium PageObjects and convert it into one of:

1. adapter-config mappings;
2. target `UiTargets`;
3. target Playwright control/page candidates;
4. manual migration notes;
5. documented suppressions.

The goal is not to blindly translate every old POM class 1:1. The goal is to preserve useful source truth: selectors, data-tid values, URLs, component hierarchy, reusable user actions, and synchronization rules.

## Missing target POM is not a blocker

If the existing Playwright target project has low POM coverage, do not immediately stop with `TICKET_NEEDED`.

Use this decision order:

1. reuse an existing target Playwright POM member when it exists and matches the source semantics;
2. generate a Playwright POM candidate/scaffold inside the allowed output or migration folder using Selenium POM selector evidence;
3. use a raw Playwright locator in generated output when selector evidence is proven and a POM scaffold is not available or not worth creating;
4. emit TODO / ticket only when selector evidence or helper semantics cannot be proven.

`ByTId("value")`, `CreateControlByTid(...)`, explicit `data-tid`, CSS, XPath, and resolved selector constants are valid selector evidence. PageObject names and property names are not.

## POM recovery loop

For every repeated source-only POM pattern:

```text
Analyze POM usage → Find POM declaration → Extract selector evidence → Check target architecture → Generate candidate → Validate → Decide
```

### 1. Analyze POM usage

Group TODO by full source expression, not only by root variable.

Bad grouping:

```text
SOURCE_ONLY_IDENTIFIER(page)
```

Good grouping:

```text
page.MenuItems.Error
page.Table.Rows.First()
page.Filter.Period.SelectValue(...)
lightbox.OkButton.Click()
```

### 2. Find source POM declaration

Search the source Selenium project for the property/method declaration.

Examples:

```csharp
public MenuItems MenuItems { get; }
public Table Table { get; }
public Button OkButton => CreateControlByTid<Button>("OkButton");
```

Also inspect helper methods such as:

```csharp
CreateControlByTid(...)
CreateControlByCss(...)
CreateControlByXPath(...)
WithDataTestId(...)
ControlFactory.Create(...)
```

A PageObject property name is not a selector. The selector must come from source truth.

### 3. Extract selector evidence

For each POM member, extract:

- source class;
- member name;
- control type;
- selector kind;
- selector value;
- parent/container relationship;
- action usage;
- assertion usage.

Example output:

```text
Source POM: CatalogPartnersAwardsPage
Member: AddButton
Control type: Button
Selector evidence: CreateControlByTid<Button>("AddButton")
Observed usage: page.AddButton.Click()
Candidate Playwright target: Page.GetByTestAttribute("AddButton")
```

### 4. Check target architecture

Before generating target POM candidates, inspect the target Playwright project.

Find existing conventions:

- base page classes;
- fixture pattern;
- ReactUI controls;
- table/list abstractions;
- modal/lightbox abstractions;
- naming conventions;
- locator helpers;
- data-test/data-tid helpers.

Do not invent a new architecture if the target project already has one.

### 5. Prefer config mappings first

If a source POM expression can be mapped safely through config, prefer config.

Examples:

```json
{
  "UiTargets": [
    {
      "Source": "page.AddButton",
      "Target": "Page.GetByTestAttribute(\"AddButton\")"
    }
  ]
}
```

or method mappings:

```json
{
  "Methods": [
    {
      "SourcePattern": "{source}.Click()",
      "TargetStatements": [
        "await {TARGET}.ClickAsync();"
      ]
    }
  ]
}
```

### 6. Generate POM candidates only inside migration folder

If config is not enough, the agent may generate target POM candidates, but only under:

```text
migration/pom-candidates/
```

The agent must not directly modify production target PageObjects unless explicitly allowed.

Candidate files are proposals, not final production code.

Example:

```text
migration/pom-candidates/
  CatalogPartnersAwardsPage.playwright.candidate.cs
  CatalogPartnersAwardsPage.mapping.md
```

Each candidate must include source evidence comments:

```csharp
// Source:
// CatalogPartnersAwardsPage.AddButton
// Selector evidence:
// CreateControlByTid<Button>("AddButton")
public ILocator AddButton => Page.GetByTestAttribute("AddButton");
```

### 7. Do not translate old POM 1:1 blindly

Old Selenium POMs often encode outdated architecture:

- too many nested page objects;
- UI implementation details;
- waits inside controls;
- Selenium-specific lifecycle;
- obsolete wrappers.

The agent should preserve source truth, not necessarily preserve the old class shape.

Acceptable transformations:

```text
Old Selenium POM property → target locator
Old Selenium control wrapper → target ReactUI control
Old navigation helper → target fixture/navigation helper
Old wait wrapper → Playwright auto-wait / explicit expect
Old modal/lightbox class → target modal component candidate
```

### 8. Suppression is allowed only after recovery attempt

Before adding broad suppressions like:

```text
page.*.*
lightbox.*.*
modal.*.*
dialog.*.*
popup.*.*
```

the agent must document:

- which POM classes were inspected;
- why selectors could not be safely translated;
- whether target architecture already replaces this layer;
- which TODO categories remain manual;
- whether generated POM candidates were created.

Suppression reason example:

```text
Suppressed page.*.* after POM recovery pass.
Reason: source Selenium POM layer does not match target Playwright control architecture.
Selectors were mined where possible and added to UiTargets.
Remaining expressions require manual target PageObject design.
```

## POM recovery report

Each migration run with POM suppression must update:

```text
migration/pom-recovery.md
```

Required sections:

```md
# POM recovery report

## Inspected source POMs

| Source POM | Members inspected | Selectors found | Candidates generated |
|---|---:|---:|---:|

## Config mappings added

| Source expression | Target expression | Evidence |
|---|---|---|

## Candidate target POMs

| Candidate file | Source POM | Status |
|---|---|---|

## Suppressions added after recovery

| Pattern | Reason | Risk |
|---|---|---|

## Manual follow-up

| Source expression | Reason |
|---|---|
```

## Decision rule

A source-only POM expression should end in one of these states:

```text
Mapped via config
Generated as target POM candidate
Documented as manual follow-up
Suppressed with evidence
Ticket created for migrator/config limitation
```

It should not disappear silently.
