# PROD-21 — Java waits/assertions matrix

## Goal

Make the experimental Java Selenium source frontend consistently lower common wait and assertion idioms into canonical legacy actions and IR V2 intents.

The key production-safety rule is that common Java waits/assertions should become structured `WaitForAction`, `TextAssertionAction` and `VisibilityAssertionAction`, not generic unsupported TODOs.

## Waits covered

- `wait.until(ExpectedConditions.visibilityOfElementLocated(By...))`
- `wait.until(ExpectedConditions.invisibilityOfElementLocated(By...))`
- `wait.until(ExpectedConditions.presenceOfElementLocated(By...))`
- `wait.until(ExpectedConditions.elementToBeClickable(By...))`
- Static-import forms such as `wait.until(visibilityOfElementLocated(By...))`
- `visibilityOf(...)`, `invisibilityOf(...)` and `elementToBeClickable(...)` over local `WebElement` variables
- Direct `driver.findElement(By...)` inside element-based wait conditions
- Inline `new WebDriverWait(...).until(...)`
- Local `WebDriverWait wait = new WebDriverWait(...)` declarations are skipped as source-only setup noise instead of producing unsupported TODOs

## Assertions covered

- JUnit expected-first text assertions:
  - `assertEquals("expected", driver.findElement(...).getText())`
  - `assertEquals("message", "expected", driver.findElement(...).getText())`
- TestNG/Assert actual-first text assertions:
  - `Assert.assertEquals(driver.findElement(...).getText(), "expected")`
- Text contains assertions:
  - `assertTrue(driver.findElement(...).getText().contains("expected"))`
- Visibility assertions:
  - `assertTrue(driver.findElement(...).isDisplayed())`
  - `assertFalse(driver.findElement(...).isDisplayed())`
- Hamcrest-style `assertThat`:
  - `assertThat(el.getText(), equalTo("expected"))`
  - `assertThat(el.getText(), containsString("expected"))`
  - `assertThat(el.isDisplayed(), is(true/false))`
- AssertJ-style `assertThat`:
  - `assertThat(el.getText()).isEqualTo("expected")`
  - `assertThat(el.getText()).contains("expected")`
  - `assertThat(el.isDisplayed()).isTrue()/isFalse()`

## Regression coverage

Added `JavaWaitsAssertionsMatrixTests` with two guards:

- `JavaParser_WaitsMatrix_ProducesStableWaitIntents`
- `JavaParser_AssertionsMatrix_ProducesTextAndVisibilityIntents`

The tests assert both legacy actions and IR V2 intent shapes (`LocatorWaitIntent`, `TextAssertionIntent`, `VisibilityAssertionIntent`).

## Production status

Java remains `experimental-mvp`: the parser is still syntax/regex based and has no Java semantic model.

This task improves the common wait/assertion matrix and reduces TODO noise, but it does not solve inheritance, overload resolution, custom assertion helpers or complex helper methods.
