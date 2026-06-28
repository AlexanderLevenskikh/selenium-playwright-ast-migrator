# PROD-09 — Java Selenium MVP fixtures and parser hardening

## Status

Implemented as an experimental MVP source frontend. Java remains non-semantic for now: the parser is regex/structure based and intentionally preserves unrecognized statements as TODO diagnostics instead of guessing.

## Added coverage

The Java Selenium frontend now recognizes:

- JUnit/TestNG-style test annotations: `@Test`, fully-qualified `@org.testng.annotations.Test`.
- Setup annotations: `@Before`, `@BeforeEach`, `@BeforeMethod`, `@BeforeClass`, `@BeforeAll`, `@BeforeSuite`.
- Direct Selenium actions:
  - `driver.get(...)`, `driver.navigate().to(...)`.
  - `driver.findElement(By.id/cssSelector/xpath/name/className/linkText/partialLinkText(...)).click()`.
  - `.sendKeys(...)`, `.clear()`.
- Local `WebElement` declarations:
  - `WebElement save = driver.findElement(...)`.
  - Follow-up `save.click()`, `save.sendKeys(...)`, `save.clear()`.
- Basic assertions:
  - `assertEquals(expected, element.getText())`.
  - `assertTrue(element.getText().contains(expected))`.
  - `assertTrue(element.isDisplayed())` / `assertFalse(element.isDisplayed())`.
- Common waits:
  - `ExpectedConditions.visibilityOfElementLocated(...)`.
  - `invisibilityOfElementLocated(...)`.
  - `presenceOfElementLocated(...)`.
  - `elementToBeClickable(...)`.
- Enter key input:
  - `sendKeys(Keys.ENTER)` / `sendKeys(Keys.RETURN)` lowers to `PressAction("Enter")`.

## Production caveat

This is still an MVP. It does not provide Java semantic/type analysis, PageFactory support, custom helper mapping, or AST-level Java parsing. Unsupported statements produce `JAVA_SELENIUM_MVP_UNRECOGNIZED_STATEMENT`.
