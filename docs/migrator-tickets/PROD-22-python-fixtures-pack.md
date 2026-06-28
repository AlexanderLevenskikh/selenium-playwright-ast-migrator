# PROD-22 — Python Selenium fixtures pack

## Goal

Expand the Python Selenium source spike beyond the initial happy path so it has realistic fixture coverage for common pytest and unittest Selenium tests.

This does **not** make Python production-ready. The frontend remains `experimental-spike`: recognition is heuristic/text-based and unsupported dynamic Python flows are still preserved as TODO diagnostics.

## Covered fixture shapes

The fixture pack now covers:

- pytest function tests: `def test_*(driver): ...`
- pytest class tests: `class Test*: def test_*(self): ...`
- unittest classes: `class TestSmoke(unittest.TestCase): ...`
- setup methods: `setUp`, `setup_method`, `setup_class`, `setup`
- `driver.get(...)` / `self.driver.get(...)`
- Selenium 4 `find_element(By.ID, "...")` action chains
- local element variables
- `self.element` variables within a parsed method
- legacy Selenium Python helpers:
  - `find_element_by_id`
  - `find_element_by_name`
  - `find_element_by_css_selector`
  - `find_element_by_xpath`
  - link/class variants
- `click()`
- `send_keys(...)`
- `clear()`
- `Keys.ENTER` / `Keys.RETURN` as `PressAction`

## Wait coverage

Recognized wait patterns include:

```python
WebDriverWait(driver, 10).until(
    EC.visibility_of_element_located((By.ID, "save"))
)

wait = WebDriverWait(driver, 10)
wait.until(EC.visibility_of(status))
wait.until(expected_conditions.element_to_be_clickable((By.ID, "save")))

WebDriverWait(driver, 5).until(
    invisibility_of_element_located((By.CSS_SELECTOR, ".loader"))
)
```

These lower to `WaitForAction` with visible/hidden/loaded intent where the condition is known.

## Assertion coverage

Recognized assertion patterns include:

```python
assert el.text == "Saved"
assert "Saved" in el.text
assert el.is_displayed()
assert not el.is_displayed()

assert driver.find_element(By.ID, "status").text == "Saved"
assert "Saved" in driver.find_element_by_css_selector(".status").text
assert not driver.find_element_by_xpath("//div[@id='error']").is_displayed()
```

## Tests

Added regression fixture tests in:

- `Migrator.Tests/PythonSeleniumFixturesPackTests.cs`

The tests cover pytest class style, unittest style, wait variables, `expected_conditions` alias/direct imports, `self.element` variables and legacy `find_element_by_*` helpers.
