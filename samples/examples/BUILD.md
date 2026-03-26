# Build Examples

How `dtk` filters `dotnet build` and `dotnet clean` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.
> **Log files:** when the output is too large to display, dtk writes the full output to a log file. Pass `--show-log` to print its path.

---

## Success — single project

**Raw** (`dotnet build samples/SampleApp/SampleApp.csproj`)

```sh
Restore complete (0.6s)
  SampleApp net10.0 succeeded (0.4s) → samples\SampleApp\bin\Debug\net10.0\SampleApp.dll
Build succeeded in 1.9s
```

**dtk** (`dtk dotnet build samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet build (1 project, 1.86s)
```

Token reduction: **3 lines → 1 line**

---

## Success — multiple projects

**Raw** (`dotnet build samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj`)

```sh
Restore complete (0.8s)
  SampleApp.Lib net10.0 succeeded (0.6s) → samples\SampleApp.Lib\bin\Debug\net10.0\SampleApp.Lib.dll
  SampleApp.MultiProject net10.0 succeeded (0.8s) → samples\SampleApp.MultiProject\bin\Debug\net10.0\SampleApp.MultiProject.dll
Build succeeded in 3.6s
```

**dtk** (`dtk dotnet build samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj`)

```sh
✓ dotnet build (2 projects, 2.78s)
```

Token reduction: **4 lines → 1 line**

---

## Single error

**Raw** (`dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`)

```sh
Restore complete (1.1s)
  SampleApp.Broken net10.0 failed with 1 error(s) (1.0s)
    D:\DotnetTokenKiller\samples\SampleApp.Broken\BrokenClass.cs(5,33): error CS0029: Cannot implicitly convert type 'string' to 'int'
Build failed with 1 error(s) in 3.2s
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

## Many errors across multiple files

**Raw** (`dotnet build samples/SampleApp.MultiError/SampleApp.MultiError.csproj`)

```sh
Restore complete (1.3s)
  SampleApp.MultiError net10.0 failed with 25 error(s) (1.5s)
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\SignatureErrors.cs(9,9): error CS1501: No overload for method 'Add' takes 3 arguments
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\SignatureErrors.cs(12,13): error CS1503: Argument 1: cannot convert from 'string' to 'int'
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\MissingTypes.cs(7,36): error CS0029: Cannot implicitly convert type 'string' to 'bool'
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\MissingTypes.cs(10,39): error CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?)
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\TypeErrors.cs(7,32): error CS0029: Cannot implicitly convert type 'string' to 'int'
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\TypeErrors.cs(10,37): error CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\AccessErrors.cs(10,34): error CS0122: 'SecretHolder._value' is inaccessible due to its protection level
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\AccessErrors.cs(13,13): error CS0122: 'SecretHolder._value' is inaccessible due to its protection level
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\AccessErrors.cs(16,9): error CS0122: 'SecretHolder.InternalHelper()' is inaccessible due to its protection level
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\UndefinedReferences.cs(9,27): error CS0103: The name 'undeclaredVariable' does not exist in the current context
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\UndefinedReferences.cs(12,9): error CS0103: The name 'MissingMethod' does not exist in the current context
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\SignatureErrors.cs(9,9): error RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\MissingTypes.cs(7,1): error RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\TypeErrors.cs(7,1): error RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\UndefinedReferences.cs(8,1): error RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\AccessErrors.cs(18,22): error CS0122: 'SecretHolder._secret' is inaccessible due to its protection level
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\AccessErrors.cs(21,22): error CS0122: 'SecretHolder._secret' is inaccessible due to its protection level
    D:\DotnetTokenKiller\samples\SampleApp.MultiError\AccessErrors.cs(24,16): error CS0122: 'SecretHolder._secret' is inaccessible due to its protection level
    ... (7 more analyzer errors: CA1822, S2325, CS0414, S1144, RCS1213, CA1823)
Build failed with 25 error(s) in 3.8s
```

**dtk** (`dtk dotnet build samples/SampleApp.MultiError/SampleApp.MultiError.csproj`)

```sh
dotnet build: 22 errors, 0 warnings
---
samples/SampleApp.MultiError/AccessErrors.cs (7 errors)
  (10,34) CS0122: 'SecretHolder._value' is inaccessible due to its protection level
  (13,13) CS0122: 'SecretHolder._value' is inaccessible due to its protection level
  (16,9) CS0122: 'SecretHolder.InternalHelper()' is inaccessible due to its protection level
  (18,22) CS0122: 'SecretHolder._secret' is inaccessible due to its protection level
  (21,22) CS0122: 'SecretHolder._secret' is inaccessible due to its protection level
  (24,16) CS0122: 'SecretHolder._secret' is inaccessible due to its protection level
  (28,9) CS0122: 'SecretHolder.InternalHelper()' is inaccessible due to its protection level
samples/SampleApp.MultiError/MissingTypes.cs (3 errors)
  (7,36) CS0029: Cannot implicitly convert type 'string' to 'bool'
  (10,39) CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?)
  (7,1) RCS1181: Convert comment to documentation comment
samples/SampleApp.MultiError/SignatureErrors.cs (3 errors)
  (9,9) CS1501: No overload for method 'Add' takes 3 arguments
  (12,13) CS1503: Argument 1: cannot convert from 'string' to 'int'
  (9,9) RCS1181: Convert comment to documentation comment
samples/SampleApp.MultiError/TypeErrors.cs (3 errors)
  (7,32) CS0029: Cannot implicitly convert type 'string' to 'int'
  (10,37) CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)
  (7,1) RCS1181: Convert comment to documentation comment
samples/SampleApp.MultiError/UndefinedReferences.cs (4 errors)
  (9,27) CS0103: The name 'undeclaredVariable' does not exist in the current context
  (12,9) CS0103: The name 'MissingMethod' does not exist in the current context
  (8,1) RCS1181: Convert comment to documentation comment
  (15,9) CS0103: The name 'NonExistentClass' does not exist in the current context
Top codes: RCS1181 (4x), S2325 (3x), CS0029 (2x), CS0266 (2x), CS0103 (3x), CS0122 (7x)
```

Token reduction: **~40 lines → 30 lines**, but crucially the errors are now grouped by file and ranked by frequency, making the root cause immediately apparent.

---

## Warnings

> **Note:** incremental builds skip re-emitting warnings, so `dotnet clean` must precede the `dtk` run to capture warning output.

**Raw** (`dotnet build samples/SampleApp.Warnings/SampleApp.Warnings.csproj`)

```sh
Restore complete (1.5s)
  SampleApp.Warnings net10.0 succeeded with 32 warning(s) (1.2s) → samples\SampleApp.Warnings\bin\Debug\net10.0\SampleApp.Warnings.dll
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(7,16): warning CA1024: Use properties where appropriate
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(22,9): warning CS0162: Unreachable code detected
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(9,13): warning CS0168: The variable 'x' is declared but never used
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(12,13): warning CS0219: The variable 'y' is assigned but its value is never used
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(16,9): warning CS0612: 'LegacyClass.LegacyMethod()' is obsolete
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(17,9): warning CS0618: 'LegacyClass.OldApi()' is obsolete: 'Use NewApi() instead.'
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(10,27): warning CS8600: Converting null literal or possible null value to non-nullable type.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\AssertionFailures.cs(20,0): warning CS8602: Dereference of a possibly null reference.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(11,16): warning CS8603: Possible null reference return.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(17,16): warning CS8603: Possible null reference return.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(19,5): warning RCS1118: Mark local variable as const
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(22,5): warning RCS1118: Mark local variable as const
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(15,5): warning RCS1118: Mark local variable as const
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(25,5): warning RCS1124: Use 'ElementAt' method or 'ElementAtOrDefault' method instead of subscript operator
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(7,5): warning RCS1124: Use 'ElementAt' method or 'ElementAtOrDefault' method instead of subscript operator
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(19,9): warning RCS1124: Use 'ElementAt' method or 'ElementAtOrDefault' method instead of subscript operator
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(10,16): warning RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(10,16): warning RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(8,5): warning RCS1181: Convert comment to documentation comment
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(15,9): warning S1123: Provide a description.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(7,5): warning S1133: Do not forget to remove this deprecated code someday.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(13,5): warning S1133: Do not forget to remove this deprecated code someday.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(7,5): warning S1481: Remove this unused variable 'unusedField'.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(20,5): warning S1481: Remove this unused variable 'temp'.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(25,5): warning S1481: Remove this unused variable 'result'.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\NullableWarnings.cs(10,5): warning S2325: Make this a 'static' method.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\UnusedCode.cs(8,5): warning S2325: Make this a 'static' method.
    D:\DotnetTokenKiller\samples\SampleApp.Warnings\ObsoleteUsage.cs(10,5): warning S2325: Make this a 'static' method.
Build succeeded with 32 warning(s) in 2.8s
```

**dtk** (`dtk dotnet build samples/SampleApp.Warnings/SampleApp.Warnings.csproj`)

```sh
dotnet build: 0 errors, 31 warnings (1 project, 2.61s)
---
CA1024 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:7 — Use properties where appropriate
CS0162 (1x)
  samples/SampleApp.Warnings/UnusedCode.cs:22 — Unreachable code detected
CS0168 (1x)
  samples/SampleApp.Warnings/UnusedCode.cs:9 — The variable 'x' is declared but never used
CS0219 (1x)
  samples/SampleApp.Warnings/UnusedCode.cs:12 — The variable 'y' is assigned but its value is never used
CS0612 (1x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:16 — 'LegacyClass.LegacyMethod()' is obsolete
CS0618 (1x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:17 — 'LegacyClass.OldApi()' is obsolete: 'Use NewApi() instead.'
CS8600 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:10 — Converting null literal or possible null value to non-nullable type.
CS8602 (1x)
  samples/SampleApp.Warnings/AssertionFailures.cs:20 — Dereference of a possibly null reference.
CS8603 (2x)
  samples/SampleApp.Warnings/NullableWarnings.cs:11 — Possible null reference return.
  samples/SampleApp.Warnings/NullableWarnings.cs:17 — Possible null reference return.
RCS1118 (3x)
  samples/SampleApp.Warnings/UnusedCode.cs:19 — Mark local variable as const
  samples/SampleApp.Warnings/ObsoleteUsage.cs:22 — Mark local variable as const
  samples/SampleApp.Warnings/UnusedCode.cs:15 — Mark local variable as const
RCS1124 (3x)
  samples/SampleApp.Warnings/UnusedCode.cs:25 — Use 'ElementAt' method or 'ElementAtOrDefault' method instead of subscript operator
  samples/SampleApp.Warnings/NullableWarnings.cs:7 — Use 'ElementAt' method or 'ElementAtOrDefault' method instead of subscript operator
  samples/SampleApp.Warnings/ObsoleteUsage.cs:19 — Use 'ElementAt' method or 'ElementAtOrDefault' method instead of subscript operator
RCS1181 (3x)
  samples/SampleApp.Warnings/NullableWarnings.cs:10 — Convert comment to documentation comment
  samples/SampleApp.Warnings/ObsoleteUsage.cs:10 — Convert comment to documentation comment
  samples/SampleApp.Warnings/UnusedCode.cs:8 — Convert comment to documentation comment
S1123 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:15 — Provide a description.
S1133 (2x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:7 — Do not forget to remove this deprecated code someday.
  samples/SampleApp.Warnings/ObsoleteUsage.cs:13 — Do not forget to remove this deprecated code someday.
S1481 (3x)
  samples/SampleApp.Warnings/UnusedCode.cs:7 — Remove this unused variable 'unusedField'.
  samples/SampleApp.Warnings/NullableWarnings.cs:20 — Remove this unused variable 'temp'.
  samples/SampleApp.Warnings/ObsoleteUsage.cs:25 — Remove this unused variable 'result'.
S2325 (3x)
  samples/SampleApp.Warnings/NullableWarnings.cs:10 — Make this a 'static' method.
  samples/SampleApp.Warnings/UnusedCode.cs:8 — Make this a 'static' method.
  samples/SampleApp.Warnings/ObsoleteUsage.cs:10 — Make this a 'static' method.
```

Token reduction: **32 warning lines → grouped-by-code list**, frequency counts highlight the most common patterns first.

---

## Clean — success

**Raw** (`dotnet clean samples/SampleApp/SampleApp.csproj`)

```sh
Build succeeded in 1.1s
```

**dtk** (`dtk dotnet clean samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet clean
```

Token reduction: **1 line → 1 line** (same length, but the dtk line is more clearly a success signal).

---

## Format — nothing to format

**Raw** (`dotnet format samples/SampleApp/SampleApp.csproj`)

```sh
(no output — dotnet format is silent when there is nothing to change)
```

**dtk** (`dtk dotnet format samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet format (nothing to format)
```

Token reduction: **0 bytes → explicit confirmation** (dtk synthesises the success signal that the raw tool omits).

---

## Format — verify-no-changes, violations found

**Raw** (`dotnet format --verify-no-changes samples/SampleApp/SampleApp.csproj`)

```sh
  D:\DotnetTokenKiller\samples\SampleApp\Program.cs(1,1): error whitespace: Fix whitespace formatting.
  D:\DotnetTokenKiller\samples\SampleApp\Service.cs(5,10): error whitespace: Fix whitespace formatting.
Format complete in 1234ms.
```

**dtk** (`dtk dotnet format --verify-no-changes samples/SampleApp/SampleApp.csproj`)

```sh
dotnet format: 2 violations
samples/SampleApp/Program.cs(1,1): error whitespace: Fix whitespace formatting.
samples/SampleApp/Service.cs(5,10): error whitespace: Fix whitespace formatting.
```

Token reduction: **absolute paths + noise → relative paths only**.
