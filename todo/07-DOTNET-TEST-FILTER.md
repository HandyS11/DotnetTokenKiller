# 07 — `dotnet test` Filter Specification

## Location

`DotnetTokenKiller.Application.Filters.DotnetTestFilter` — implements `IOutputFilter` from the Domain layer.

## `dotnet test` Output Analysis

The `dotnet test` command produces very verbose output. This is the **highest-value filter** because test output is the most frequent LLM interaction in development.

### Raw Output (All Pass) — ~30 lines

```
  Determining projects to restore...
  All projects are up-to-date for restore.
  MyProject -> D:\projects\MyProject\bin\Debug\net10.0\MyProject.dll
  MyProject.Tests -> D:\projects\MyProject.Tests\bin\Debug\net10.0\MyProject.Tests.dll
Test run for D:\projects\MyProject.Tests\bin\Debug\net10.0\MyProject.Tests.dll (.NETCoreApp,Version=v10.0)
Microsoft (R) Test Execution Command Line Tool Version 17.9.0 (x64)
Copyright (c) Microsoft Corporation.  All rights reserved.

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42, Duration: 1.234 s - MyProject.Tests.dll (net10.0)
```

### Raw Output (Failures) — ~60+ lines

```
  Determining projects to restore...
  All projects are up-to-date for restore.
  MyProject -> D:\projects\MyProject\bin\Debug\net10.0\MyProject.dll
  MyProject.Tests -> D:\projects\MyProject.Tests\bin\Debug\net10.0\MyProject.Tests.dll
Test run for D:\projects\MyProject.Tests\bin\Debug\net10.0\MyProject.Tests.dll (.NETCoreApp,Version=v10.0)
Microsoft (R) Test Execution Command Line Tool Version 17.9.0 (x64)
Copyright (c) Microsoft Corporation.  All rights reserved.

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

  Failed UserServiceTests.CreateUser_DuplicateEmail_ThrowsConflict [42 ms]
  Error Message:
   Expected exception of type 'ConflictException' but got 'InvalidOperationException'.
  Stack Trace:
     at MyProject.Tests.UserServiceTests.CreateUser_DuplicateEmail_ThrowsConflict() in D:\projects\MyProject.Tests\UserServiceTests.cs:line 45

  Failed OrderServiceTests.PlaceOrder_InsufficientStock_ReturnsError [18 ms]
  Error Message:
   Assert.Equal() Failure
           Expected: "InsufficientStock"
           Actual:   "OutOfStock"
  Stack Trace:
     at MyProject.Tests.OrderServiceTests.PlaceOrder_InsufficientStock_ReturnsError() in D:\projects\MyProject.Tests\OrderServiceTests.cs:line 78

Failed!  - Failed:     2, Passed:    40, Skipped:     0, Total:    42, Duration: 1.567 s - MyProject.Tests.dll (net10.0)
```

### Raw Output (Multiple Test Projects) — ~80+ lines

Multiple `Passed!`/`Failed!` summary lines, one per test assembly.

---

## Filtering Strategy

### All Pass — Ultra-Compact

**Input**: ~30 lines
**Output**: `✓ dotnet test: 42 passed (1 project, 1.23s)`
**Savings**: ~93%

For multiple projects:
**Output**: `✓ dotnet test: 142 passed (3 projects, 4.56s)`

### With Failures — Failures + Summary

**Input**: ~60 lines
**Output**:
```
FAILURES (2):
═══════════════════════════════════════
1. UserServiceTests.CreateUser_DuplicateEmail_ThrowsConflict [42ms]
   Expected exception of type 'ConflictException' but got 'InvalidOperationException'.
   at UserServiceTests.cs:45

2. OrderServiceTests.PlaceOrder_InsufficientStock_ReturnsError [18ms]
   Assert.Equal() Failure — Expected: "InsufficientStock", Actual: "OutOfStock"
   at OrderServiceTests.cs:78

dotnet test: 2 failed, 40 passed (1 project, 1.57s)
```
**Savings**: ~75%

### All Skipped / Filtered Out

**Output**: `✓ dotnet test: 0 passed, 5 skipped (1 project, 0.12s)`

---

## Parsing Requirements

### Regex Patterns Needed

| Pattern | Purpose | Example Match |
|---|---|---|
| Summary line | Aggregate results per assembly | `Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 1.234 s - Tests.dll (net10.0)` |
| Failed test header | Identify each failure | `  Failed TestClass.TestMethod [42 ms]` |
| Stack trace with file | Extract source location | `at Namespace.Class.Method() in D:\path\File.cs:line 45` |

### Data to Extract

**Per test assembly (summary line)**:
- Failed count, passed count, skipped count, total count
- Duration in seconds
- Assembly name

**Per test failure**:
- Fully qualified test name
- Duration in milliseconds
- Error message (between "Error Message:" and "Stack Trace:")
- Source file and line number (first stack frame with a file reference)

### Aggregation

- Sum results across all test assemblies
- Collect all failure details
- Limit displayed failures to 15 (with "+N more" overflow)

---

## Lines to Skip (Noise)

| Line Pattern | Why Skip |
|---|---|
| `Determining projects to restore...` | Build preamble |
| `All projects are up-to-date for restore.` | Build preamble |
| `Restored ...` | Build preamble |
| `Project -> D:\path\output.dll` | Build preamble |
| `Test run for ...` | Test runner header |
| `Microsoft (R) Test Execution...` | Copyright notice |
| `Copyright (c) Microsoft...` | Copyright notice |
| `Starting test execution...` | Progress noise |
| `A total of N test files...` | Progress noise |

---

## Edge Cases

| Scenario | Expected Output |
|---|---|
| 0 tests found | `✓ dotnet test: 0 tests found` |
| Build failure prevents tests | Show build errors (delegate to build filter or show raw) |
| `--logger trx` additional output | Ignore TRX logger noise |
| `--blame-crash` with dump | Keep crash info, strip dump path noise |
| Multiple TFMs (e.g., net10.0 + net8.0) | Aggregate across TFMs: "142 passed (3 projects, 2 TFMs)" |
| `dotnet test --filter "Category=Unit"` | Include filter info in summary |
| Very long test names | Truncate at reasonable length |
| Nested/parameterized tests | Preserve parameter info in display |

---

## Error Message Formatting

Different test framework assertion messages need to be compacted:

| Framework | Raw Message Style | Compact Version |
|---|---|---|
| xUnit `Assert.Equal` | Multi-line with Expected/Actual | Single line: `Expected: "X", Actual: "Y"` |
| xUnit `Assert.Throws` | `Expected exception of type...` | Pass through (already compact) |
| FluentAssertions | `Expected X to be Y because...` | Truncate at 200 chars |
| NUnit `Assert.That` | `Expected: X But was: Y` | Pass through |

The filter should normalize multi-line assertion messages into single lines where possible, and always truncate at 200 characters.
