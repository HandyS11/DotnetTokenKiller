Feature: Calculator
  A simple calculator for demonstrating Reqnroll BDD test output.

  Scenario: Add two numbers
    Given the first number is 50
    And the second number is 70
    When the two numbers are added
    Then the result should be 120

  Scenario: Subtract two numbers
    Given the first number is 100
    And the second number is 30
    When the second number is subtracted from the first
    Then the result should be 70

  Scenario: Multiply two numbers
    Given the first number is 6
    And the second number is 7
    When the two numbers are multiplied
    Then the result should be 42

  Scenario: Division by zero should fail
    Given the first number is 10
    And the second number is 0
    When the first number is divided by the second
    Then the result should be 0
