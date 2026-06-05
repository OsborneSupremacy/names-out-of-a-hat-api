---
applyTo: "src/GiftExchange.Library.*/**/*"
---

## Use of Null

> "I call it my billion-dollar mistake. It was the invention of the null reference in 1965." - Tony Hoare

* When a null value comes from an external source, such as a request body or a database query result, but as soon as it's recognized as null, convert it to an empty string, empty collection, min value (in the case of numbers, dates, etc.) or some other appropriate default value (not null).
* Do not return null from a method or property.
* Do not assign a null to a field, property, or variable.
* Record types should not have nullable properties.

## Unit Testing Guildelines

* Do not use `.ConfigureAwait(false)` in unit tests. It is not necessary and can bypass parallization configuration in test runners.`