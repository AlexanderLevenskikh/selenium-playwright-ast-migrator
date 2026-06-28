# PROD-19 — Java Selenium fixtures pack

## Goal

Turn the Java Selenium MVP from a narrow parser spike into a measurable fixture-backed source frontend.
The goal is not full Java production support yet; it is to lock representative Selenium/JUnit/TestNG patterns so future parser work has clear regression coverage.

## Covered fixture families

- JUnit 4 `@Before` / `@Test`.
- JUnit 5 `@BeforeEach` / `@Test`.
- TestNG `@BeforeMethod` / `@Test`.
- Direct `driver.get(...)` and `driver.navigate().to(...)` setup navigation.
- Direct `driver.findElement(By...)` click/fill/clear.
- Local `WebElement` declarations followed by variable-based actions/assertions.
- Class-level and method-local `By` variables used through `driver.findElement(locatorVariable)`.
- `WebDriverWait` / `ExpectedConditions` for visible, hidden, loaded and clickable states.
- Basic assertion matrix: `assertEquals(... getText())`, `assertTrue(... contains(...))`, `assertTrue/False(... isDisplayed())`.
- Unsupported project-specific helpers preserved as `UnsupportedAction` with `JAVA_SELENIUM_MVP_UNRECOGNIZED_STATEMENT`.

## Parser hardening added

`JavaSeleniumTestFileParser` now keeps a simple locator-symbol table for Java `By` declarations:

```java
private final By saveButton = By.id("save");
By nameInput = By.name("name");

driver.findElement(saveButton).click();
driver.findElement(nameInput).sendKeys("Alex");
wait.until(ExpectedConditions.visibilityOfElementLocated(saveButton));
```

The symbol table is intentionally syntax-based and limited. It is suitable for the experimental Java MVP but should be replaced or supplemented with a semantic Java frontend if Java support becomes production-grade.

## Production status

Java remains `experimental-mvp` after this task. The fixtures improve confidence in common patterns, but Page Object methods, PageFactory, inheritance, overload resolution and cross-file type analysis are still outside the MVP.
