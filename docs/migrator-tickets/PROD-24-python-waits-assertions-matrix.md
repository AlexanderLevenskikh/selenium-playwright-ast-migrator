# PROD-24. Python waits/assertions matrix

## Goal

Harden the experimental Python Selenium frontend so common pytest/unittest waits and assertions lower into canonical legacy actions and IR V2 intents instead of unsupported TODOs.

Python support remains `experimental-spike`: the parser is still heuristic/text based and does not perform Python semantic/type resolution.

## Covered waits

Recognized `WebDriverWait(...).until(...)` / `wait.until(...)` / `wait.until_not(...)` forms:

- `EC.visibility_of_element_located((By.ID, "save"))`
- static/direct import `visibility_of_element_located(...)`
- `EC.invisibility_of_element_located(...)`
- `EC.presence_of_element_located(...)`
- `EC.presence_of_all_elements_located(...)`
- `EC.element_to_be_clickable(...)`
- `EC.visibility_of(element)` / `EC.invisibility_of(element)` / `EC.staleness_of(element)`
- direct `driver.find_element(...)` inside `visibility_of` / `invisibility_of` / `element_to_be_clickable`
- legacy `find_element_by_*` inside direct wait element forms
- `EC.text_to_be_present_in_element((By.ID, "status"), "Saved")`
- `EC.text_to_be_present_in_element_value(...)`

## Covered assertions

Recognized pytest-style assertions:

- `assert el.text == "Saved"`
- `assert el.text != "Error"`
- `assert "Saved" in el.text`
- `assert "Error" not in el.text`
- `assert el.is_displayed()`
- `assert not el.is_displayed()`
- direct `driver.find_element(...).text == "Saved"`
- direct `"Saved" in driver.find_element(...).text`
- direct `driver.find_element(...).is_displayed()`

Recognized unittest-style assertions:

- `self.assertEqual("Saved", el.text)`
- `self.assertEqual(el.text, "Saved")`
- `self.assertIn("Saved", el.text)`
- `self.assertNotIn("Error", el.text)`
- `self.assertTrue(el.is_displayed())`
- `self.assertFalse(el.is_displayed())`
- direct `self.assertEqual("Saved", driver.find_element(...).text)`
- direct `self.assertTrue/False(driver.find_element(...).is_displayed())`

## IR V2 contract

The regression tests assert that Python source lowers into:

- `LocatorWaitIntent`
- `TextAssertionIntent`
- `VisibilityAssertionIntent`

This keeps Python source behavior aligned with Java/C# wait/assertion hardening while still reporting Python as experimental.

## Limitations

- No Python semantic model or import graph.
- Negative contains currently lowers to the closest existing legacy text assertion shape (`TextNotEquals`) because there is no dedicated `TextNotContains` action yet.
- Complex fixture graphs, helper methods, custom expected conditions and indirect POMs still require TODO review.
