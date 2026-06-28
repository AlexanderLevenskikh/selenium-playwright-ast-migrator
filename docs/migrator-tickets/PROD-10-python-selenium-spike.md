# PROD-10 — Python Selenium spike

## Status

Implemented as an experimental source frontend for common pytest/unittest-style Selenium tests.

## Added coverage

The Python Selenium frontend recognizes:

- pytest function tests: `def test_...`.
- unittest/pytest setup methods: `setUp`, `setup_method`, `setup_class`, `setup`.
- Direct Selenium actions:
  - `driver.get(...)`.
  - `driver.find_element(By.ID/CSS_SELECTOR/XPATH/NAME/CLASS_NAME/LINK_TEXT/PARTIAL_LINK_TEXT, "...").click()`.
  - `.send_keys(...)`, `.clear()`.
- Local element declarations:
  - `save = driver.find_element(...)`.
  - Follow-up `save.click()`, `save.send_keys(...)`, `save.clear()`.
- Basic assertions:
  - `assert element.is_displayed()` / `assert not element.is_displayed()`.
  - `assert element.text == expected`.
  - `assert expected in element.text`.
- Common waits:
  - `WebDriverWait(...).until(EC.visibility_of_element_located((By.ID, "...")))`.
  - `invisibility_of_element_located`, `presence_of_element_located`, `element_to_be_clickable`.
- Enter key input:
  - `send_keys(Keys.ENTER)` / `send_keys(Keys.RETURN)` lowers to `PressAction("Enter")`.

## Production caveat

This is a spike, not a complete Python parser. It does not execute Python, resolve imports/types, understand fixtures semantically, or parse arbitrary expressions. Unsupported statements produce `PYTHON_SELENIUM_SPIKE_UNRECOGNIZED_STATEMENT`.
