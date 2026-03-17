# Test Examples

How `dtk` filters `dotnet test` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.
> **Log files:** when the output is too large to display, dtk writes the full output to a log file. Pass `--show-log` to print its path.

---

## xUnit — single failure

**Raw** (`dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```sh
Restore complete (0.7s)
  SampleApp.Tests net10.0 succeeded (0.4s) → samples\SampleApp.Tests\bin\Debug\net10.0\SampleApp.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.4)
[xUnit.net 00:00:00.17]   Discovering: SampleApp.Tests
[xUnit.net 00:00:00.28]   Discovered:  SampleApp.Tests
[xUnit.net 00:00:00.34]   Starting:    SampleApp.Tests
     Warning:
     The component "Fluent Assertions" is governed by the rules defined in the Xceed License Agreement and
     the Xceed Fluent Assertions Community License. You may use Fluent Assertions free of charge for
     non-commercial use only. An active subscription is required to use Fluent Assertions for commercial use.
     Please contact Xceed Sales mailto:sales@xceed.com to acquire a subscription at a very low cost.
     A paid commercial license supports the development and continued increasing support of
     Fluent Assertions users under both commercial and community licenses. Help us
     keep Fluent Assertions at the forefront of unit testing.
     For more information, visit https://xceed.com/products/unit-testing/fluent-assertions/
[xUnit.net 00:00:00.48]     SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [FAIL]
[xUnit.net 00:00:00.48]       Intentional failure
[xUnit.net 00:00:00.48]       Stack Trace:
[xUnit.net 00:00:00.49]         D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs(8,0): at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails()
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:00.56]   Finished:    SampleApp.Tests
  SampleApp.Tests test net10.0 failed with 1 error(s) (0.5s)
    D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs(8): error TESTERROR:
          SampleApp.Tests.IntentionallyFailingTests.AlwaysFails (5ms):
            Error Message: Intentional failure
            Stack Trace:
               at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails() in D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs:line 8
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Test summary: total: 4, failed: 1, succeeded: 3, skipped: 0, duration: 0.5s
Build failed with 1 error(s) in 5.4s
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.07s)
```

Token reduction: **~35 lines → 5 lines**

---

## xUnit — many failure types (FluentAssertions)

**Raw** (`dotnet test samples/SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj`)

```sh
Restore complete (0.5s)
  SampleApp.Tests.MultiFailure net10.0 succeeded (1.1s)
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.4)
[xUnit.net 00:00:00.17]   Discovering: SampleApp.Tests.MultiFailure
[xUnit.net 00:00:00.28]   Discovered:  SampleApp.Tests.MultiFailure
[xUnit.net 00:00:00.34]   Starting:    SampleApp.Tests.MultiFailure
     Warning:
     The component "Fluent Assertions" is governed by the rules …
[xUnit.net 00:00:00.48]     SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation [FAIL]
[xUnit.net 00:00:00.48]       System.InvalidOperationException : Simulated invalid-operation during test
[xUnit.net 00:00:00.48]       Stack Trace:
[xUnit.net 00:00:00.49]         D:\DotnetTokenKiller\samples\SampleApp.Tests.MultiFailure\ExceptionTests.cs(11,0): at SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation()
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:00.49]     SampleApp.Tests.MultiFailure.ExceptionTests.Throws_NotImplemented [FAIL]
[xUnit.net 00:00:00.49]       System.NotImplementedException : Feature not yet implemented
     … (11 more [FAIL] blocks, each 5–15 lines with full absolute paths and reflection internals)
[xUnit.net 00:00:00.58]   Finished:    SampleApp.Tests.MultiFailure
  SampleApp.Tests.MultiFailure test net10.0 failed with 12 error(s) (2.3s)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MultiFailure\ExceptionTests.cs(11): error TESTERROR: …
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MultiFailure\ExceptionTests.cs(23): error TESTERROR: …
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MultiFailure\ExceptionTests.cs(30): error TESTERROR: …
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MultiFailure\DataDrivenFailures.cs(17): error TESTERROR: …
     …
Test summary: total: 21, failed: 12, succeeded: 9, skipped: 0, duration: 0.6s
Build failed with 12 error(s) in 6.1s
```

> The full raw output is ~250 lines (33 KB), including xUnit adapter headers, FluentAssertions license banners, full absolute paths, and reflection call-stacks for every failure.

**dtk** (`dtk dotnet test samples/SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj`)

```sh
FAILURES (12):
  SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation [5 ms]
    System.InvalidOperationException : Simulated invalid-operation during test
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 11
  SampleApp.Tests.MultiFailure.ExceptionTests.Index_Out_Of_Range [1 ms]
    System.ArgumentOutOfRangeException : Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 30
  SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 10, expected: 99, shouldPass: True) [4 ms]
    Expected: 99, Actual:   100
    at samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 17
  SampleApp.Tests.MultiFailure.AssertionFailures.Equality_Mismatch [79 ms]
    Expected actual to be 99 because we want to show a numeric mismatch, but found 42 (difference of -57).
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 13
  SampleApp.Tests.MultiFailure.AssertionFailures.Collection_Missing_Element [10 ms]
    Expected list {"alpha", "beta", "gamma"} to contain "delta" because the collection was expected to include delta.
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 27
  SampleApp.Tests.MultiFailure.AssertionFailures.Object_Should_Not_Be_Null [2 ms]
    Expected value not to be <null> because a non-null value was expected.
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 41
  SampleApp.Tests.MultiFailure.AssertionFailures.Bool_Expected_True [2 ms]
    Expected condition to be True because a true flag was expected, but found False.
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 34
  SampleApp.Tests.MultiFailure.AssertionFailures.String_Should_Match_But_Doesnt [4 ms]
    Expected actual to be a match with the expectation because this demonstrates string diff output, but it differs at index 6: ↓ (actual) "Hello World" "Hello Mars" ↑ (expected)
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 20
dotnet test: 12 failed, 9 passed (1 project, 0.11s)
```

Token reduction: **~250 lines (33 KB) → 28 lines**

---

## NUnit — multiple failures

**Raw** (`dotnet test samples/SampleApp.Tests.NUnit/SampleApp.Tests.NUnit.csproj`)

```sh
Restore complete (0.6s)
  SampleApp.Tests.NUnit net10.0 succeeded (0.5s)
NUnit Adapter 5.0.0.0: Test execution started
NUnit3TestExecutor discovered 8 of 8 NUnit test cases using Current Discovery mode, Non-Explicit mode, run-as-app: False
  SampleApp.Tests.NUnit test net10.0 failed with 3 error(s) (0.5s)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.NUnit\FailingTests.cs(11): error TESTERROR:
          SampleApp.Tests.NUnit.FailingTests.Equality_Fails (5ms):
            Error Message: Assert.That(actual, Is.EqualTo(expected))
              Expected: 100
              But was:  42
            Stack Trace:
               at SampleApp.Tests.NUnit.FailingTests.Equality_Fails() in D:\DotnetTokenKiller\samples\SampleApp.Tests.NUnit\FailingTests.cs:line 11
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.NUnit\FailingTests.cs(18): error TESTERROR:
          SampleApp.Tests.NUnit.FailingTests.Collection_Should_Contain_Missing_Item (60ms):
            Error Message: Assert.That(list, Does.Contain("delta"))
              Expected: collection containing "delta"
              But was:  < "alpha", "beta", "gamma" >
            Stack Trace:
               at SampleApp.Tests.NUnit.FailingTests.Collection_Should_Contain_Missing_Item() in D:\DotnetTokenKiller\samples\SampleApp.Tests.NUnit\FailingTests.cs:line 18
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.NUnit\FailingTests.cs(25): error TESTERROR:
          SampleApp.Tests.NUnit.FailingTests.Unexpected_Exception (3ms):
            Error Message: System.InvalidOperationException : Intentional exception from NUnit test
            Stack Trace:
               at SampleApp.Tests.NUnit.FailingTests.Unexpected_Exception() in D:\DotnetTokenKiller\samples\SampleApp.Tests.NUnit\FailingTests.cs:line 25
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Test summary: total: 8, failed: 3, succeeded: 5, skipped: 0, duration: 0.5s
Build failed with 3 error(s) in 4.0s
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests.NUnit/SampleApp.Tests.NUnit.csproj`)

```sh
FAILURES (3):
  SampleApp.Tests.NUnit.FailingTests.Collection_Should_Contain_Missing_Item [60 ms]
    Assert.That(list, Does.Contain("delta"))
    at samples/SampleApp.Tests.NUnit/FailingTests.cs:line 18
  SampleApp.Tests.NUnit.FailingTests.Equality_Fails [5 ms]
    Assert.That(actual, Is.EqualTo(expected))
    at samples/SampleApp.Tests.NUnit/FailingTests.cs:line 11
  SampleApp.Tests.NUnit.FailingTests.Unexpected_Exception [3 ms]
    System.InvalidOperationException : Intentional exception from NUnit test
    at samples/SampleApp.Tests.NUnit/FailingTests.cs:line 25
dotnet test: 3 failed, 5 passed (1 project, 0.08s)
```

Token reduction: **~35 lines → 10 lines**

---

## MSTest — multiple failures

**Raw** (`dotnet test samples/SampleApp.Tests.MSTest/SampleApp.Tests.MSTest.csproj`)

```sh
Restore complete (0.5s)
  SampleApp.Tests.MSTest net10.0 succeeded (0.5s)
  SampleApp.Tests.MSTest test net10.0 failed with 3 error(s) (0.5s)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MSTest\FailingTests.cs(11): error TESTERROR:
          SampleApp.Tests.MSTest.FailingTests.Equality_Fails (34ms):
            Error Message: Assert.AreEqual failed. Expected:<100>. Actual:<42>.
            Stack Trace:
               at SampleApp.Tests.MSTest.FailingTests.Equality_Fails() in D:\DotnetTokenKiller\samples\SampleApp.Tests.MSTest\FailingTests.cs:line 11
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MSTest\FailingTests.cs(18): error TESTERROR:
          SampleApp.Tests.MSTest.FailingTests.Null_Check_Fails (< 1ms):
            Error Message: Assert.IsNotNull failed.
            Stack Trace:
               at SampleApp.Tests.MSTest.FailingTests.Null_Check_Fails() in D:\DotnetTokenKiller\samples\SampleApp.Tests.MSTest\FailingTests.cs:line 18
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
    D:\DotnetTokenKiller\samples\SampleApp.Tests.MSTest\FailingTests.cs(24): error TESTERROR:
          SampleApp.Tests.MSTest.FailingTests.Wrong_Exception_Type (1ms):
            Error Message: Test method threw exception System.InvalidOperationException, but exception System.ArgumentException was expected. Exception message: Thrown intentionally.
            Stack Trace:
               at SampleApp.Tests.MSTest.FailingTests.Wrong_Exception_Type() in D:\DotnetTokenKiller\samples\SampleApp.Tests.MSTest\FailingTests.cs:line 24
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Test summary: total: 8, failed: 3, succeeded: 5, skipped: 0, duration: 0.5s
Build failed with 3 error(s) in 4.6s
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests.MSTest/SampleApp.Tests.MSTest.csproj`)

```sh
FAILURES (3):
  SampleApp.Tests.MSTest.FailingTests.Equality_Fails [34 ms]
    Assert.AreEqual failed. Expected:<100>. Actual:<42>.
    at samples/SampleApp.Tests.MSTest/FailingTests.cs:line 11
  SampleApp.Tests.MSTest.FailingTests.Wrong_Exception_Type [1 ms]
    Test method threw exception System.InvalidOperationException, but exception System.ArgumentException was expected. Exception message: Thrown intentionally.
    at samples/SampleApp.Tests.MSTest/FailingTests.cs:line 24
dotnet test: 3 failed, 5 passed (1 project, 0.08s)
```

Token reduction: **~35 lines → 8 lines**

---

## Reqnroll (BDD) — Gherkin scenario failures

**Raw** (`dotnet test samples/SampleApp.Tests.Reqnroll/SampleApp.Tests.Reqnroll.csproj`)

```sh
Restore complete (0.7s)
  SampleApp.Tests.Reqnroll net10.0 succeeded (1.2s)
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.4)
[xUnit.net 00:00:00.17]   Discovering: SampleApp.Tests.Reqnroll
[xUnit.net 00:00:00.28]   Discovered:  SampleApp.Tests.Reqnroll
[xUnit.net 00:00:00.34]   Starting:    SampleApp.Tests.Reqnroll
Given I have a string "abc"
-> done: StringSteps.GivenIHaveAString("abc") (0.0s)
When I reverse it
-> done: StringSteps.WhenIReverseIt() (0.0s)
Then the result should be "cba"
-> done: StringSteps.ThenTheResultShouldBe("cba") (0.0s)
Given I have a string "hello"
-> done: StringSteps.GivenIHaveAString("hello") (0.0s)
When I reverse it
-> done: StringSteps.WhenIReverseIt() (0.0s)
Then the result should be "xyz"
-> error: StringSteps.ThenTheResultShouldBe("xyz") (0.0s)
  Error Message:
    Xunit.Sdk.EqualException: Assert.Equal() Failure: Strings differ
    Expected: "xyz"
    Actual:   "olleh"
  Stack Trace:
   at SampleApp.Tests.Reqnroll.Steps.StringSteps.ThenTheResultShouldBe(String expected) in D:\DotnetTokenKiller\samples\SampleApp.Tests.Reqnroll\Steps\StringSteps.cs:line 32
Given I enter 10 and 2
-> done: CalculatorSteps.GivenIEnterAnd(10, 2) (0.0s)
When I divide A by B
-> done: CalculatorSteps.WhenIDivideAByB() (0.0s)
Then the result should be 5
-> done: CalculatorSteps.ThenTheResultShouldBe(5) (0.0s)
Given I enter 10 and 0
-> done: CalculatorSteps.GivenIEnterAnd(10, 0) (0.0s)
When I divide A by B
-> error: CalculatorSteps.WhenIDivideAByB() (0.0s)
  Error Message:
    System.DivideByZeroException : Attempted to divide by zero.
  Stack Trace:
   at SampleApp.Tests.Reqnroll.Steps.CalculatorSteps.WhenIDivideAByB() in D:\DotnetTokenKiller\samples\SampleApp.Tests.Reqnroll\Steps\CalculatorSteps.cs:line 32
Then the result should be 0
-> skipped because of previous errors
  SampleApp.Tests.Reqnroll test net10.0 failed with 2 error(s) (1.3s)
    …
Test summary: total: 7, failed: 2, succeeded: 5, skipped: 0, duration: 0.2s
Build failed with 2 error(s) in 5.3s
```

> Reqnroll produces especially verbose output: each Gherkin step is traced individually, even passing steps. The raw output grows proportionally to the number of scenarios, not just to the number of failures.

**dtk** (`dtk dotnet test samples/SampleApp.Tests.Reqnroll/SampleApp.Tests.Reqnroll.csproj`)

```sh
FAILURES (2):
  Intentionally wrong expectation [90 ms]
    Assert.Equal() Failure: Strings differ Expected: "xyz" Actual: "cba"
    at samples/SampleApp.Tests.Reqnroll/Steps/StringSteps.cs:line 32
  Division by zero should fail [86 ms]
    System.DivideByZeroException : Attempted to divide by zero.
    at samples/SampleApp.Tests.Reqnroll/Steps/CalculatorSteps.cs:line 32
dotnet test: 2 failed, 5 passed (1 project, 0.18s)
```

Token reduction: **~60 lines → 7 lines** — the step-by-step trace for all passing scenarios is completely stripped.
