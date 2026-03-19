---
name: dtk
description: 'Use `dtk` (DotnetTokenKiller) instead of raw `dotnet` commands to reduce token usage when building, testing, restoring, or cleaning .NET projects.'
---

# DotnetTokenKiller (dtk) — AI Agent Usage Guide

`dtk` is a CLI proxy that wraps `dotnet` commands and filters their output down to actionable signal only. When an AI agent works with .NET projects, using `dtk` instead of raw `dotnet` saves 50-97% of tokens by stripping SDK banners, ANSI escapes, MSBuild noise, progress lines, and duplicate diagnostics.

## When to Use

Use `dtk` as a drop-in replacement for `dotnet` whenever you run:

| Instead of              | Use                          |
|-------------------------|------------------------------|
| `dotnet build`          | `dtk dotnet build`           |
| `dotnet test`           | `dtk dotnet test`            |
| `dotnet restore`        | `dtk dotnet restore`         |
| `dotnet clean`          | `dtk dotnet clean`           |

All positional arguments and flags are forwarded unchanged:

```sh
dtk dotnet build --configuration Release
dtk dotnet test --filter "Category=Unit"
dtk dotnet build src/MyProject/MyProject.csproj
```

Unknown `dotnet` subcommands (e.g. `dotnet run`, `dotnet publish`) are passed through to `dotnet` unchanged.

## Installation

```sh
dotnet tool install -g DotnetTokenKiller
```

Requires .NET 10 SDK.

## Output Format by Command

### Build — success

```
✓ dotnet build (1 project, 1.86s)
```

### Build — errors

Errors are grouped by file with workspace-relative paths and ranked by frequency:

```
dotnet build: 22 errors, 0 warnings
---
src/MyProject/Foo.cs (7 errors)
  (10,34) CS0122: 'SecretHolder._value' is inaccessible due to its protection level
  (13,13) CS0122: 'SecretHolder._value' is inaccessible due to its protection level
src/MyProject/Bar.cs (3 errors)
  (7,36) CS0029: Cannot implicitly convert type 'string' to 'bool'
Top codes: CS0122 (7x), CS0029 (2x)
```

### Build — warnings

Warnings are grouped by diagnostic code with frequency counts:

```
dotnet build: 0 errors, 31 warnings (1 project, 2.61s)
---
CS8603 (2x)
  src/Foo.cs:11 — Possible null reference return.
  src/Foo.cs:17 — Possible null reference return.
RCS1118 (3x)
  src/Bar.cs:19 — Mark local variable as const
```

### Test — success

```
✓ dotnet test: 21 passed (1 project, 0.11s)
```

### Test — failures

Failures list the fully-qualified test name, duration, error message, and workspace-relative file location:

```
FAILURES (12):
  MyTests.ExceptionTests.Throws_InvalidOperation [5 ms]
    System.InvalidOperationException : Simulated invalid-operation during test
    at src/MyTests/ExceptionTests.cs:line 11
  MyTests.AssertionFailures.Equality_Mismatch [79 ms]
    Expected actual to be 99, but found 42.
    at src/MyTests/AssertionFailures.cs:line 13
dotnet test: 12 failed, 9 passed (1 project, 0.11s)
```

### Restore — errors

```
dotnet restore: 1 error
  NU1101: Unable to find package Foo.DoesNotExist. (src/MyProject/MyProject.csproj)
```

### Clean — success

```
✓ dotnet clean
```

## Useful Flags

| Flag          | Purpose                                                     |
|---------------|-------------------------------------------------------------|
| `--show-log`  | Print the path to the full unfiltered log file after a run  |
| `-v`          | Increase verbosity (can be repeated: `-v -v`)               |

Use `--show-log` when you need to inspect the full raw output that was filtered away.

## Token Savings Analytics

Check cumulative token savings:

```sh
dtk gain               # last 30 days
dtk gain --days 7      # last 7 days
dtk gain --project     # current project only
dtk gain --json        # machine-readable JSON output
```

## Key Behaviors

- **Paths are workspace-relative**: Absolute paths like `D:\MyProject\src\Foo.cs` become `src/Foo.cs`.
- **Errors grouped by file**: Build errors are clustered per file, not scattered in MSBuild order.
- **Frequency ranking**: `Top codes:` line shows the most common error codes first.
- **Test framework agnostic**: Works with xUnit, NUnit, MSTest, and Reqnroll (BDD).
- **Incremental build caveat**: Warnings are only emitted when the file is recompiled. Run `dtk dotnet clean` first if you need a full warning report.
- **Exit codes preserved**: `dtk` propagates the `dotnet` exit code, so CI pipelines work correctly.
