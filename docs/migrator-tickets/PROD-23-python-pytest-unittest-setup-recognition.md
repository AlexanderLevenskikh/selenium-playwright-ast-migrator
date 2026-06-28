# PROD-23 — Python pytest/unittest setup recognition

## Status

Done — experimental Python Selenium frontend now recognizes common setup shapes more honestly and carries simple setup-backed element variables into test methods.

## Scope

Covered setup methods:

- `def setup_method(self): ...`
- `def setup_method(self, method): ...`
- `def setup_class(cls): ...`
- `def setUp(self): ...` for `unittest.TestCase`
- `def setup_function(...): ...` compatibility shape
- `def setUpClass(cls): ...` compatibility shape
- existing `def setup(...): ...` compatibility shape

## Implemented behavior

Setup method bodies are still emitted as `TestFileModel.SetUpActions`, but their simple Selenium element declarations are also indexed for tests in the same parsed file.

Examples now recognized:

```python
class TestLogin:
    def setup_method(self, method):
        self.driver.get("/login")
        self.save_button = self.driver.find_element(By.ID, "save")
        self.wait = WebDriverWait(self.driver, 10)

    def test_save(self):
        self.save_button.click()
        self.wait.until(EC.visibility_of(self.save_button))
        assert self.save_button.text == "Saved"
```

`self.save_button` is resolved from setup and lowered to normal actions/intents:

- `ClickAction`
- `PressAction` for `Keys.ENTER` / `Keys.RETURN`
- `WaitForAction`
- `VisibilityAssertionAction`
- `TextAssertionAction`

`setup_class(cls)` also seeds `cls.*` declarations so tests may reference them through either `cls.*` or `self.*` in simple cases.

## Boilerplate suppression

Common setup boilerplate is ignored instead of producing noisy unsupported TODOs:

```python
self.driver = webdriver.Chrome()
cls.driver = webdriver.Chrome()
super().setUp()
self.wait = WebDriverWait(self.driver, 10)
```

## Limitations

This is still an experimental spike, not full Python fixture graph support:

- no import graph analysis;
- no pytest fixture dependency graph;
- no type inference;
- no inheritance-aware setup merging;
- setup context is file-level/simple-name based, so complex multi-class files can still require manual review.

## Regression coverage

Added `PythonSetupRecognitionTests` with coverage for:

- `setup_method(self, method)` seeding `self.*` locators into test bodies;
- `setup_class(cls)` seeding `cls.*` locators into `self.*` test references;
- `unittest.TestCase.setUp` suppressing driver boilerplate and seeding locators.
