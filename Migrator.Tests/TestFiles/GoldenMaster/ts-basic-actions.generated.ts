import { test, expect } from '@playwright/test';

// Generated from Selenium C# source: BasicActions.cs
// Target: Playwright TypeScript (experimental). Validate inside a real TS Playwright project.

test('ClickFillAndAssert', async ({ page }) => {
  await page.locator('[data-tid=\'save-button\']').click();
  await page.locator('[data-tid=\'name-input\']').fill("Alex");
  expect(actualToast).toEqual("Saved");
  // TODO: page.LegacyHelper.DoSomething(); [MIGRATOR:RAW_STATEMENT]
  //   Reason: Raw Selenium/C# statement is not target-safe TypeScript.
  //   Next: Add a TS-specific mapping/profile rule or leave it for manual migration.
});
