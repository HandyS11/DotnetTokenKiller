# Test Examples

How `dtk` filters `dotnet test` output across test frameworks.

> **How to read these examples.** Every pair below is captured from a real run against the
> projects in `samples/`, and `ExamplesBindingTests` replays each _Raw_ block through the real
> filter on every build, so a page cannot drift from what the tool actually prints. Absolute paths
> are rewritten to `/repo` to keep the captures machine-independent, and a non-zero exit code is
> noted beside the command that produced it.

> **Log files:** when the output is too large to display, dtk writes the full output to a log
> file. Pass `--show-log` to print its path.

---

## xUnit - single failure

**Raw** (`dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`) — exit code 1

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
  SampleApp.Tests -> /repo/samples/SampleApp.Tests/bin/Debug/net10.0/SampleApp.Tests.dll
Test run for /repo/samples/SampleApp.Tests/bin/Debug/net10.0/SampleApp.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [2 ms]
  Error Message:
   Intentional failure
  Stack Trace:
     at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails() in /repo/samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 26 ms - SampleApp.Tests.dll (net10.0)
[xUnit.net 00:00:00.15]     SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [FAIL]
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [2 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.03s)
```

Token reduction: **14 lines -> 5 lines**

---

## xUnit - many failure types (FluentAssertions)

Stack traces are dropped and each failure is reduced to its assertion message.

**Raw** (`dotnet test samples/SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj`) — exit code 1

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
  SampleApp.Tests.MultiFailure -> /repo/samples/SampleApp.Tests.MultiFailure/bin/Debug/net10.0/SampleApp.Tests.MultiFailure.dll
Test run for /repo/samples/SampleApp.Tests.MultiFailure/bin/Debug/net10.0/SampleApp.Tests.MultiFailure.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation [1 ms]
  Error Message:
   System.InvalidOperationException : Simulated invalid-operation during test
  Stack Trace:
     at SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation() in /repo/samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 11
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.ExceptionTests.Throws_NotImplemented [< 1 ms]
  Error Message:
   System.NotImplementedException : Feature not yet implemented
  Stack Trace:
     at SampleApp.Tests.MultiFailure.ExceptionTests.Throws_NotImplemented() in /repo/samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 23
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.ExceptionTests.Index_Out_Of_Range [< 1 ms]
  Error Message:
   System.ArgumentOutOfRangeException : Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
  Stack Trace:
     at System.Collections.Generic.List`1.get_Item(Int32 index)
   at SampleApp.Tests.MultiFailure.ExceptionTests.Index_Out_Of_Range() in /repo/samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 30
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 10, expected: 99, shouldPass: True) [1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 99
Actual:   100
  Stack Trace:
     at SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(Int32 input, Int32 expected, Boolean shouldPass) in /repo/samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 17
   at InvokeStub_DataDrivenFailures.Square_Matches_Expected(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 5, expected: 24, shouldPass: True) [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 24
Actual:   25
  Stack Trace:
     at SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(Int32 input, Int32 expected, Boolean shouldPass) in /repo/samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 17
   at InvokeStub_DataDrivenFailures.Square_Matches_Expected(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed SampleApp.Tests.MultiFailure.ExceptionTests.Throws_ArgumentNull [< 1 ms]
  Error Message:
   System.ArgumentNullException : Simulated null argument (Parameter 'param')
  Stack Trace:
     at SampleApp.Tests.MultiFailure.ExceptionTests.Throws_ArgumentNull() in /repo/samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 17
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.DataDrivenFailures.String_Length_Matches(input: "", expected: 1) [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 1
Actual:   0
  Stack Trace:
     at SampleApp.Tests.MultiFailure.DataDrivenFailures.String_Length_Matches(String input, Int32 expected) in /repo/samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 27
   at InvokeStub_DataDrivenFailures.String_Length_Matches(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed SampleApp.Tests.MultiFailure.AssertionFailures.Equality_Mismatch [34 ms]
  Error Message:
   Expected actual to be 99 because we want to show a numeric mismatch, but found 42 (difference of -57).
  Stack Trace:
     at FluentAssertions.Numeric.NumericAssertionsBase`3.Be(T expected, String because, Object[] becauseArgs)
   at SampleApp.Tests.MultiFailure.AssertionFailures.Equality_Mismatch() in /repo/samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 13
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.AssertionFailures.Collection_Missing_Element [5 ms]
  Error Message:
   Expected list {"alpha", "beta", "gamma"} to contain "delta" because the collection was expected to include delta.
  Stack Trace:
     at FluentAssertions.Collections.GenericCollectionAssertions`3.Contain(T expected, String because, Object[] becauseArgs)
   at SampleApp.Tests.MultiFailure.AssertionFailures.Collection_Missing_Element() in /repo/samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 27
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.AssertionFailures.Object_Should_Not_Be_Null [< 1 ms]
  Error Message:
   Expected value not to be <null> because a non-null value was expected.
  Stack Trace:
     at FluentAssertions.Primitives.ReferenceTypeAssertions`2.NotBeNull(String because, Object[] becauseArgs)
   at SampleApp.Tests.MultiFailure.AssertionFailures.Object_Should_Not_Be_Null() in /repo/samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 41
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.AssertionFailures.Bool_Expected_True [1 ms]
  Error Message:
   Expected condition to be True because a true flag was expected, but found False.
  Stack Trace:
     at FluentAssertions.Primitives.BooleanAssertions`1.BeTrue(String because, Object[] becauseArgs)
   at SampleApp.Tests.MultiFailure.AssertionFailures.Bool_Expected_True() in /repo/samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 34
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SampleApp.Tests.MultiFailure.AssertionFailures.String_Should_Match_But_Doesnt [1 ms]
  Error Message:
   Expected actual to be a match with the expectation because this demonstrates string diff output, but it differs at index 6:
         ↓ (actual)
  "Hello World"
  "Hello Mars"
         ↑ (expected)
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Be(String expected, String because, Object[] becauseArgs)
   at SampleApp.Tests.MultiFailure.AssertionFailures.String_Should_Match_But_Doesnt() in /repo/samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 20
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:    12, Passed:     9, Skipped:     0, Total:    21, Duration: 48 ms - SampleApp.Tests.MultiFailure.dll (net10.0)
[xUnit.net 00:00:00.18]     SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation [FAIL]
[xUnit.net 00:00:00.18]     SampleApp.Tests.MultiFailure.ExceptionTests.Throws_NotImplemented [FAIL]
[xUnit.net 00:00:00.18]     SampleApp.Tests.MultiFailure.ExceptionTests.Index_Out_Of_Range [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 10, expected: 99, shouldPass: True) [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 5, expected: 24, shouldPass: True) [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.ExceptionTests.Throws_ArgumentNull [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.DataDrivenFailures.String_Length_Matches(input: "", expected: 1) [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.AssertionFailures.Equality_Mismatch [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.AssertionFailures.Collection_Missing_Element [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.AssertionFailures.Object_Should_Not_Be_Null [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.AssertionFailures.Bool_Expected_True [FAIL]
[xUnit.net 00:00:00.20]     SampleApp.Tests.MultiFailure.AssertionFailures.String_Should_Match_But_Doesnt [FAIL]
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj`)

```sh
FAILURES (12):
  SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation [1 ms]
    System.InvalidOperationException : Simulated invalid-operation during test
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 11
  SampleApp.Tests.MultiFailure.ExceptionTests.Throws_NotImplemented [< 1 ms]
    System.NotImplementedException : Feature not yet implemented
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 23
  SampleApp.Tests.MultiFailure.ExceptionTests.Index_Out_Of_Range [< 1 ms]
    System.ArgumentOutOfRangeException : Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 30
  SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 10, expected: 99, shouldPass: True) [1 ms]
    Expected: 99, Actual:   100
    at samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 17
  SampleApp.Tests.MultiFailure.DataDrivenFailures.Square_Matches_Expected(input: 5, expected: 24, shouldPass: True) [< 1 ms]
    Expected: 24, Actual:   25
    at samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 17
  SampleApp.Tests.MultiFailure.ExceptionTests.Throws_ArgumentNull [< 1 ms]
    System.ArgumentNullException : Simulated null argument (Parameter 'param')
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 17
  SampleApp.Tests.MultiFailure.DataDrivenFailures.String_Length_Matches(input: "", expected: 1) [< 1 ms]
    Expected: 1, Actual:   0
    at samples/SampleApp.Tests.MultiFailure/DataDrivenFailures.cs:line 27
  SampleApp.Tests.MultiFailure.AssertionFailures.Equality_Mismatch [34 ms]
    Expected actual to be 99 because we want to show a numeric mismatch, but found 42 (difference of -57).
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 13
  SampleApp.Tests.MultiFailure.AssertionFailures.Collection_Missing_Element [5 ms]
    Expected list {"alpha", "beta", "gamma"} to contain "delta" because the collection was expected to include delta.
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 27
  SampleApp.Tests.MultiFailure.AssertionFailures.Object_Should_Not_Be_Null [< 1 ms]
    Expected value not to be <null> because a non-null value was expected.
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 41
  SampleApp.Tests.MultiFailure.AssertionFailures.Bool_Expected_True [1 ms]
    Expected condition to be True because a true flag was expected, but found False.
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 34
  SampleApp.Tests.MultiFailure.AssertionFailures.String_Should_Match_But_Doesnt [1 ms]
    Expected actual to be a match with the expectation because this demonstrates string diff output, but it differs at index 6: ↓ (actual) "Hello World" "Hello Mars" ↑ (expected)
    at samples/SampleApp.Tests.MultiFailure/AssertionFailures.cs:line 20
dotnet test: 12 failed, 9 passed (1 project, 0.05s)
```

Token reduction: **118 lines -> 38 lines**

---

## NUnit - multiple failures

**Raw** (`dotnet test samples/SampleApp.Tests.NUnit/SampleApp.Tests.NUnit.csproj`) — exit code 1

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
  SampleApp.Tests.NUnit -> /repo/samples/SampleApp.Tests.NUnit/bin/Debug/net10.0/SampleApp.Tests.NUnit.dll
Test run for /repo/samples/SampleApp.Tests.NUnit/bin/Debug/net10.0/SampleApp.Tests.NUnit.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed Collection_Should_Contain_Missing_Item [23 ms]
  Error Message:
     Assert.That(list, Does.Contain("delta"))
  Expected: some item equal to "delta"
  But was:  < "alpha", "beta", "gamma" >

  Stack Trace:
     at SampleApp.Tests.NUnit.FailingTests.Collection_Should_Contain_Missing_Item() in /repo/samples/SampleApp.Tests.NUnit/FailingTests.cs:line 18

1)    at SampleApp.Tests.NUnit.FailingTests.Collection_Should_Contain_Missing_Item() in /repo/samples/SampleApp.Tests.NUnit/FailingTests.cs:line 18


  Failed Equality_Fails [2 ms]
  Error Message:
     Values should match but they don't
Assert.That(42, Is.EqualTo(99))
  Expected: 99
  But was:  42

  Stack Trace:
     at SampleApp.Tests.NUnit.FailingTests.Equality_Fails() in /repo/samples/SampleApp.Tests.NUnit/FailingTests.cs:line 11

1)    at SampleApp.Tests.NUnit.FailingTests.Equality_Fails() in /repo/samples/SampleApp.Tests.NUnit/FailingTests.cs:line 11


  Failed Unexpected_Exception [1 ms]
  Error Message:
   System.InvalidOperationException : Something went wrong in the test
  Stack Trace:
     at SampleApp.Tests.NUnit.FailingTests.Unexpected_Exception() in /repo/samples/SampleApp.Tests.NUnit/FailingTests.cs:line 24
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)


Failed!  - Failed:     3, Passed:     5, Skipped:     0, Total:     8, Duration: 29 ms - SampleApp.Tests.NUnit.dll (net10.0)
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests.NUnit/SampleApp.Tests.NUnit.csproj`)

```sh
FAILURES (3):
  Collection_Should_Contain_Missing_Item [23 ms]
    Assert.That(list, Does.Contain("delta")) Expected: some item equal to "delta" But was:  < "alpha", "beta", "gamma" >
    at samples/SampleApp.Tests.NUnit/FailingTests.cs:line 18
  Equality_Fails [2 ms]
    Values should match but they don't Assert.That(42, Is.EqualTo(99)) Expected: 99 But was:  42
    at samples/SampleApp.Tests.NUnit/FailingTests.cs:line 11
  Unexpected_Exception [1 ms]
    System.InvalidOperationException : Something went wrong in the test
    at samples/SampleApp.Tests.NUnit/FailingTests.cs:line 24
dotnet test: 3 failed, 5 passed (1 project, 0.03s)
```

Token reduction: **30 lines -> 11 lines**

---

## MSTest - multiple failures

**Raw** (`dotnet test samples/SampleApp.Tests.MSTest/SampleApp.Tests.MSTest.csproj`) — exit code 1

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
  SampleApp.Tests.MSTest -> /repo/samples/SampleApp.Tests.MSTest/bin/Debug/net10.0/SampleApp.Tests.MSTest.dll
Test run for /repo/samples/SampleApp.Tests.MSTest/bin/Debug/net10.0/SampleApp.Tests.MSTest.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed Equality_Fails [12 ms]
  Error Message:
   Assert.AreEqual failed. Expected:<100>. Actual:<42>. Expected values to be equal
  Stack Trace:
     at SampleApp.Tests.MSTest.FailingTests.Equality_Fails() in /repo/samples/SampleApp.Tests.MSTest/FailingTests.cs:line 11
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

  Failed Null_Check_Fails [< 1 ms]
  Error Message:
   Assert.IsNull failed. Value was expected to be null
  Stack Trace:
     at SampleApp.Tests.MSTest.FailingTests.Null_Check_Fails() in /repo/samples/SampleApp.Tests.MSTest/FailingTests.cs:line 18
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

  Failed Wrong_Exception_Type [< 1 ms]
  Error Message:
   Test method threw exception System.InvalidOperationException, but exception System.ArgumentException was expected. Exception message: System.InvalidOperationException: Wrong exception type
  Stack Trace:
     at SampleApp.Tests.MSTest.FailingTests.Wrong_Exception_Type() in /repo/samples/SampleApp.Tests.MSTest/FailingTests.cs:line 26
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)


Failed!  - Failed:     3, Passed:     5, Skipped:     0, Total:     8, Duration: 31 ms - SampleApp.Tests.MSTest.dll (net10.0)
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests.MSTest/SampleApp.Tests.MSTest.csproj`)

```sh
FAILURES (3):
  Equality_Fails [12 ms]
    Assert.AreEqual failed. Expected:<100>. Actual:<42>. Expected values to be equal
    at samples/SampleApp.Tests.MSTest/FailingTests.cs:line 11
  Null_Check_Fails [< 1 ms]
    Assert.IsNull failed. Value was expected to be null
    at samples/SampleApp.Tests.MSTest/FailingTests.cs:line 18
  Wrong_Exception_Type [< 1 ms]
    Test method threw exception System.InvalidOperationException, but exception System.ArgumentException was expected. Exception message: System.InvalidOperationException: Wrong exception type
    at samples/SampleApp.Tests.MSTest/FailingTests.cs:line 26
dotnet test: 3 failed, 5 passed (1 project, 0.03s)
```

Token reduction: **27 lines -> 11 lines**

---

## Reqnroll (BDD) - Gherkin scenario failures

**Raw** (`dotnet test samples/SampleApp.Tests.Reqnroll/SampleApp.Tests.Reqnroll.csproj`) — exit code 1

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
  SampleApp.Tests.Reqnroll -> /repo/samples/SampleApp.Tests.Reqnroll/bin/Debug/net10.0/SampleApp.Tests.Reqnroll.dll
Test run for /repo/samples/SampleApp.Tests.Reqnroll/bin/Debug/net10.0/SampleApp.Tests.Reqnroll.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed Division by zero should fail [33 ms]
  Error Message:
   System.DivideByZeroException : Attempted to divide by zero.
  Stack Trace:
     at SampleApp.Tests.Reqnroll.StepDefinitions.CalculatorSteps.WhenDivided() in /repo/samples/SampleApp.Tests.Reqnroll/StepDefinitions/CalculatorSteps.cs:line 32
   at InvokeStub_Action`1.Invoke(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
--- End of stack trace from previous location ---
   at Reqnroll.Bindings.BindingInvoker.InvokeBindingAsync(IBinding binding, IContextManager contextManager, Object[] arguments, ITestTracer testTracer, DurationHolder durationHolder)
   at Reqnroll.Infrastructure.TestExecutionEngine.ExecuteStepMatchAsync(BindingMatch match, Object[] arguments, DurationHolder durationHolder)
   at Reqnroll.Infrastructure.TestExecutionEngine.ExecuteStepAsync(IContextManager contextManager, StepInstance stepInstance)
   at Reqnroll.Infrastructure.TestExecutionEngine.OnAfterLastStepAsync()
   at Reqnroll.TestRunner.CollectScenarioErrorsAsync()
   at SampleApp.Tests.Reqnroll.Features.CalculatorFeature.ScenarioCleanupAsync()
   at SampleApp.Tests.Reqnroll.Features.CalculatorFeature.DivisionByZeroShouldFail() in /repo/samples/SampleApp.Tests.Reqnroll/Features/Calculator.feature:line 26
--- End of stack trace from previous location ---
  Standard Output Messages:
 Given the first number is 10
 -> done: CalculatorSteps.GivenTheFirstNumberIs(10) (0.0s)
 And the second number is 0
 -> done: CalculatorSteps.GivenTheSecondNumberIs(0) (0.0s)
 When the first number is divided by the second
 -> error: Attempted to divide by zero. (0.0s)
 Then the result should be 0
 -> skipped because of previous errors


  Failed Intentionally wrong expectation [33 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
           ↓ (pos 0)
Expected: "xyz"
Actual:   "cba"
           ↑ (pos 0)
  Stack Trace:
     at SampleApp.Tests.Reqnroll.StepDefinitions.StringSteps.ThenTheResultStringShouldBe(String expected) in /repo/samples/SampleApp.Tests.Reqnroll/StepDefinitions/StringSteps.cs:line 32
   at InvokeStub_Action`2.Invoke(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
--- End of stack trace from previous location ---
   at Reqnroll.Bindings.BindingInvoker.InvokeBindingAsync(IBinding binding, IContextManager contextManager, Object[] arguments, ITestTracer testTracer, DurationHolder durationHolder)
   at Reqnroll.Infrastructure.TestExecutionEngine.ExecuteStepMatchAsync(BindingMatch match, Object[] arguments, DurationHolder durationHolder)
   at Reqnroll.Infrastructure.TestExecutionEngine.ExecuteStepAsync(IContextManager contextManager, StepInstance stepInstance)
   at Reqnroll.Infrastructure.TestExecutionEngine.OnAfterLastStepAsync()
   at Reqnroll.TestRunner.CollectScenarioErrorsAsync()
   at SampleApp.Tests.Reqnroll.Features.StringManipulationFeature.ScenarioCleanupAsync()
   at SampleApp.Tests.Reqnroll.Features.StringManipulationFeature.IntentionallyWrongExpectation() in /repo/samples/SampleApp.Tests.Reqnroll/Features/StringManipulation.feature:line 17
--- End of stack trace from previous location ---
  Standard Output Messages:
 Given the string "abc"
 -> done: StringSteps.GivenTheString("abc") (0.0s)
 When the string is reversed
 -> done: StringSteps.WhenReversed() (0.0s)
 Then the result string should be "xyz"
 -> error: Assert.Equal() Failure: Strings differ
            ↓ (pos 0)
 Expected: "xyz"
 Actual:   "cba"
            ↑ (pos 0) (0.0s)



Failed!  - Failed:     2, Passed:     5, Skipped:     0, Total:     7, Duration: 66 ms - SampleApp.Tests.Reqnroll.dll (net10.0)
[xUnit.net 00:00:00.33]     Division by zero should fail [FAIL]
[xUnit.net 00:00:00.33]     Intentionally wrong expectation [FAIL]
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests.Reqnroll/SampleApp.Tests.Reqnroll.csproj`)

```sh
FAILURES (2):
  Division by zero should fail [33 ms]
    System.DivideByZeroException : Attempted to divide by zero.
    at samples/SampleApp.Tests.Reqnroll/StepDefinitions/CalculatorSteps.cs:line 32
  Intentionally wrong expectation [33 ms]
    Expected: "xyz", Actual:   "cba"
    at samples/SampleApp.Tests.Reqnroll/StepDefinitions/StringSteps.cs:line 32
dotnet test: 2 failed, 5 passed (1 project, 0.07s)
```

Token reduction: **64 lines -> 8 lines**
