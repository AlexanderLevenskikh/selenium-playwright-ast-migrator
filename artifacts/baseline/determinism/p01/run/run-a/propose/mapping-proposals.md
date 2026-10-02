# Mapping Proposals

## Summary

| Priority | Count |
|---|---:|
| High | 0 |
| Medium | 0 |
| Low | 3 |
| **Total** | **3** |

## Top proposals

1. **Add UiTarget mapping for `WebDriver.FindElement(By.Id("login"))`** (Low, score: 7)
2. **Add UiTarget mapping for `WebDriver.FindElement(By.Id("password"))`** (Low, score: 7)
3. **Add UiTarget mapping for `WebDriver.FindElement(By.Id("username"))`** (Low, score: 7)

## Proposals by kind

### UiTarget (3 proposal(s))

## Add UiTarget mapping for `WebDriver.FindElement(By.Id("login"))`

- **Id:** `UIT-1`
- **Kind:** UiTarget
- **Priority:** Low
- **Confidence:** Medium
- **Occurrences:** 1
- **Score:** 7
- **RequiresSourceTruth:** True

**Evidence:**

Source expression `WebDriver.FindElement(By.Id("login"))` appears 1 time(s) across 1 file(s). Not mapped in current adapter config.

**Affected files:**

- LoginTests.cs

**Reason:**

WebDriver.FindElement(By.Id("login")) is used as a locator target but is not mapped. Each occurrence produces a TODO comment in generated code.

**Suggested config snippet:**

```json
{
  "SourceExpression": "WebDriver.FindElement(By.Id("login"))",
  "TargetExpression": "<SOURCE_TRUTH_REQUIRED>",
  "TargetKind": "TestId",
  "TestIdAttribute": "<SOURCE_TRUTH_REQUIRED>"
}
```

**Risks:**

Applying without source truth will generate invalid selectors. Verify selector via PageObject inspection.

**Next action:**

Inspect PageObject classes for `WebDriver.FindElement(By.Id("login"))`. Search for corresponding WithDataTestId/WithDataTest/WithDataTid. Add UiTarget mapping to the narrowest scope.

> **Agent constraints:** Do not invent selectors. Use PageObject/source truth before applying this proposal. Add mapping to the narrowest scope. Run analyze/migrate/verify after applying.

---

## Add UiTarget mapping for `WebDriver.FindElement(By.Id("password"))`

- **Id:** `UIT-2`
- **Kind:** UiTarget
- **Priority:** Low
- **Confidence:** Medium
- **Occurrences:** 1
- **Score:** 7
- **RequiresSourceTruth:** True

**Evidence:**

Source expression `WebDriver.FindElement(By.Id("password"))` appears 1 time(s) across 1 file(s). Not mapped in current adapter config.

**Affected files:**

- LoginTests.cs

**Reason:**

WebDriver.FindElement(By.Id("password")) is used as a locator target but is not mapped. Each occurrence produces a TODO comment in generated code.

**Suggested config snippet:**

```json
{
  "SourceExpression": "WebDriver.FindElement(By.Id("password"))",
  "TargetExpression": "<SOURCE_TRUTH_REQUIRED>",
  "TargetKind": "TestId",
  "TestIdAttribute": "<SOURCE_TRUTH_REQUIRED>"
}
```

**Risks:**

Applying without source truth will generate invalid selectors. Verify selector via PageObject inspection.

**Next action:**

Inspect PageObject classes for `WebDriver.FindElement(By.Id("password"))`. Search for corresponding WithDataTestId/WithDataTest/WithDataTid. Add UiTarget mapping to the narrowest scope.

> **Agent constraints:** Do not invent selectors. Use PageObject/source truth before applying this proposal. Add mapping to the narrowest scope. Run analyze/migrate/verify after applying.

---

## Add UiTarget mapping for `WebDriver.FindElement(By.Id("username"))`

- **Id:** `UIT-3`
- **Kind:** UiTarget
- **Priority:** Low
- **Confidence:** Medium
- **Occurrences:** 1
- **Score:** 7
- **RequiresSourceTruth:** True

**Evidence:**

Source expression `WebDriver.FindElement(By.Id("username"))` appears 1 time(s) across 1 file(s). Not mapped in current adapter config.

**Affected files:**

- LoginTests.cs

**Reason:**

WebDriver.FindElement(By.Id("username")) is used as a locator target but is not mapped. Each occurrence produces a TODO comment in generated code.

**Suggested config snippet:**

```json
{
  "SourceExpression": "WebDriver.FindElement(By.Id("username"))",
  "TargetExpression": "<SOURCE_TRUTH_REQUIRED>",
  "TargetKind": "TestId",
  "TestIdAttribute": "<SOURCE_TRUTH_REQUIRED>"
}
```

**Risks:**

Applying without source truth will generate invalid selectors. Verify selector via PageObject inspection.

**Next action:**

Inspect PageObject classes for `WebDriver.FindElement(By.Id("username"))`. Search for corresponding WithDataTestId/WithDataTest/WithDataTid. Add UiTarget mapping to the narrowest scope.

> **Agent constraints:** Do not invent selectors. Use PageObject/source truth before applying this proposal. Add mapping to the narrowest scope. Run analyze/migrate/verify after applying.

---

