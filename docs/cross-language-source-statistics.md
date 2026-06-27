# Selenium source-language prioritization

This document is intentionally pragmatic: exact Selenium-test language share is hard to measure globally, so prioritization combines supported Selenium bindings with broader developer-language popularity signals.

## Supported Selenium ecosystem languages

Common Selenium/WebDriver overviews list client APIs/bindings for Java, C#, Ruby, JavaScript/Node.js, R and Python. Selenium is therefore naturally multi-language; a production migrator should not encode C# as the only possible source language.

## Priority recommendation

| Priority | Source language | Why |
|---:|---|---|
| 1 | C# Selenium | Existing production source; preserve all current behavior. |
| 2 | Java Selenium | Very common enterprise Selenium language; API shape is close to C# Selenium; good first cross-language proof. |
| 3 | Python Selenium | Very common automation language; validates dynamic-language parsing and pytest-style fixtures. |
| 4 | JavaScript/TypeScript Selenium | Important web/testing ecosystem, but async and same-language target concerns make it better after IR V2 is stronger. |
| 5 | Ruby Selenium/Capybara | Supported ecosystem, but likely lower priority unless a consuming team needs it. |

## Practical conclusion

The roadmap should explicitly add **Python Selenium** after Java. Java proves source-neutral IR with a familiar OO/Selenium shape; Python proves the architecture against dynamic language idioms.
