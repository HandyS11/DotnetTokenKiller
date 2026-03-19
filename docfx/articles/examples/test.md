# Test Examples

How `dtk` filters `dotnet test` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.
> **Log files:** when the output is too large to display, DTK writes the full output to a log file. Pass `--show-log` to print its path.

---

## xUnit — Single Failure

**Raw** (`dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```text
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
     ...
[xUnit.net 00:00:00.48]     SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [FAIL]
[xUnit.net 00:00:00.48]       Intentional failure
[xUnit.net 00:00:00.48]       Stack Trace:
[xUnit.net 00:00:00.49]         D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs(8,0): ...
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(...)
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(...)
[xUnit.net 00:00:00.56]   Finished:    SampleApp.Tests
  SampleApp.Tests test net10.0 failed with 1 error(s) (0.5s)
    D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs(8): error TESTERROR:
          SampleApp.Tests.IntentionallyFailingTests.AlwaysFails (5ms):
            Error Message: Intentional failure
            Stack Trace:
               at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails() in ...
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(...)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(...)
Test summary: total: 4, failed: 1, succeeded: 3, skipped: 0, duration: 0.5s
Build failed with 1 error(s) in 5.4s
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```text
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.07s)
```

Token reduction: **~35 lines → 5 lines**

What gets stripped:

- xUnit adapter version banner
- Discovery/start/finish lifecycle messages
- Fluent Assertions license warning
- Full absolute paths (replaced with workspace-relative)
- Framework reflection internals in stack traces
- Duplicate error reporting (xUnit format + MSBuild format)

---

## xUnit — Many Failures (FluentAssertions)

**Raw** (`dotnet test samples/SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj`)

> The full raw output is ~250 lines (33 KB), including xUnit adapter headers, FluentAssertions license banners, full absolute paths, and reflection call-stacks for every failure.

**dtk** (`dtk dotnet test samples/SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj`)

```text
FAILURES (12):
  SampleApp.Tests.MultiFailure.ExceptionTests.Throws_InvalidOperation [3 ms]
    System.InvalidOperationException: Simulated invalid-operation during test
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 11
  SampleApp.Tests.MultiFailure.ExceptionTests.Throws_NotImplemented [< 1 ms]
    System.NotImplementedException: Feature not yet implemented
    at samples/SampleApp.Tests.MultiFailure/ExceptionTests.cs:line 23
  ...
dotnet test: 12 failed, 9 passed (1 project, 0.12s)
```

Key improvements:

- Each failure shows: fully qualified test name, duration, error message, and one clean stack frame
- Reflection internals (`MethodBaseInvoker`, `InvokeWithNoArgs`) stripped
- Absolute paths replaced with workspace-relative paths
- Fluent Assertions license banner removed
- xUnit discovery/lifecycle messages removed
- Summary line shows pass/fail counts and timing
