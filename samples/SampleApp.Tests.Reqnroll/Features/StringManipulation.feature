Feature: String Manipulation
  Exercises string operations to produce a mix of passing and failing scenarios.

  Scenario: Concatenate two strings
    Given the string "Hello"
    When " World" is appended
    Then the result string should be "Hello World"

  Scenario: Uppercase conversion
    Given the string "hello"
    When the string is converted to uppercase
    Then the result string should be "HELLO"

  Scenario: Intentionally wrong expectation
    Given the string "abc"
    When the string is reversed
    Then the result string should be "xyz"
