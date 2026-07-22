# Standard run final gate

A high-confidence PASS requires:

- a completed standard run with generated files and reports;
- no generated syntax errors;
- no hidden/empty-test or assertion-suppression workaround;
- a fresh matching `verify-project` PASS when project compilation is required;
- explicit disclosure of remaining TODO, unsupported actions, and runtime risks.

A missing SDK, unavailable target project, or CLI crash is a blocker or limitation. Never create a result file manually to satisfy this gate.
