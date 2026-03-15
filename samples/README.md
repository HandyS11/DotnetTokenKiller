# Samples

This folder contains minimal .NET projects used as fixtures for integration tests and to demonstrate how **DotnetTokenKiller (dtk)** reduces the noise in `dotnet` command output before it reaches an LLM.

## Projects

| Project | Purpose |
|---|---|
| `SampleApp` | A clean, compilable console app — used to showcase success scenarios |
| `SampleApp.Broken` | Contains a deliberate type-error (`CS0029`) — used to showcase error filtering |
| `SampleApp.Tests` | xUnit test project with 3 passing tests and 1 intentionally failing test |
| `SampleApp.BadPackage` | References a non-existent NuGet package (`DotnetTokenKiller.DoesNotExist`) — used to showcase restore errors |

---

## Why dtk?

When piping `dotnet` output to an LLM you pay for every token. The examples below show the same command run with and without `dtk` so you can judge the noise reduction yourself.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints; the _dtk_ block is what you would send to your LLM.

---

### `dotnet build` — success

**Raw** (`dotnet build samples/SampleApp/SampleApp.csproj`)

```sh
Restore complete (0.2s)
  SampleApp net10.0 succeeded (0.1s) → samples/SampleApp/bin/Debug/net10.0/SampleApp.dll
Build succeeded in 0.5s
```

**dtk** (`dtk dotnet build samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet build (1 project, 0.51s)
```

---

### `dotnet build` — with errors

**Raw** (`dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`)

```sh
Restore complete (0.2s)
  SampleApp.Broken net10.0 failed with 1 error(s) (0.1s)
    /home/user/Dev/DotnetTokenKiller/samples/SampleApp.Broken/BrokenClass.cs
    (5,33): error CS0029: Cannot implicitly convert type 'string' to 'int'
Build failed with 1 error(s) in 0.6s
```

**dtk** (`dtk dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`)

```sh
dotnet build: 1 error, 0 warnings
---
samples/SampleApp.Broken/BrokenClass.cs (1 error)
  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

---

### `dotnet test` — with failures

**Raw** (`dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```sh
Restore complete (0.2s)
  SampleApp.Tests net10.0 succeeded (0.1s) → samples/SampleApp.Tests/bin/Debug/net10.0/SampleApp.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.5)
[xUnit.net 00:00:00.03]   Discovering: SampleApp.Tests
[xUnit.net 00:00:00.05]   Discovered:  SampleApp.Tests
[xUnit.net 00:00:00.07]   Starting:    SampleApp.Tests
     Warning:
     The component "Fluent Assertions" is governed by the rules defined in the Xceed License Agreement and
     the Xceed Fluent Assertions Community License. You may use Fluent Assertions free of charge for
     non-commercial use only. An active subscription is required to use Fluent Assertions for commercial use.
     Please contact Xceed Sales mailto:sales@xceed.com to acquire a subscription at a very low cost.
     ...
[xUnit.net 00:00:00.09]     SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [FAIL]
[xUnit.net 00:00:00.09]       Intentional failure
[xUnit.net 00:00:00.09]       Stack Trace:
[xUnit.net 00:00:00.09]         /home/user/Dev/DotnetTokenKiller/samples/SampleApp.Tests/IntentionallyFailingTests.cs(8,0): at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails()
[xUnit.net 00:00:00.09]            at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
[xUnit.net 00:00:00.09]            at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:00.10]   Finished:    SampleApp.Tests
  SampleApp.Tests test net10.0 failed with 1 error(s) (0.5s)
    /home/user/Dev/DotnetTokenKiller/samples/SampleApp.Tests/IntentionallyFailingTests.cs(8): error TESTERROR:
          SampleApp.Tests.IntentionallyFailingTests.AlwaysFails (1ms):
            Error Message: Intentional failure
            Stack Trace:
               at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails() in /home/user/Dev/DotnetTokenKiller/samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Test summary: total: 4, failed: 1, succeeded: 3, skipped: 0, duration: 0.5s
Build failed with 1 error(s) in 1.0s
```

**dtk** (`dtk dotnet test samples/SampleApp.Tests/SampleApp.Tests.csproj`)

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [1 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.02s)
```

---

### `dotnet restore` — missing package

**Raw** (`dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```sh
    /home/user/Dev/DotnetTokenKiller/samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj :
    error NU1101: Unable to find package DotnetTokenKiller.DoesNotExist.
    No packages exist with this id in source(s): nuget.org
Restore failed with 1 error(s) in 0.8s
```

**dtk** (`dtk dotnet restore samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj`)

```sh
dotnet restore: 1 error
  NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): nuget.org (samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj)
```

---

### `dotnet clean` — success

**Raw** (`dotnet clean samples/SampleApp/SampleApp.csproj`)

```sh
Build succeeded in 0.2s
```

**dtk** (`dtk dotnet clean samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet clean
```
