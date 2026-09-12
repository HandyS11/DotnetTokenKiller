# Build & Clean Examples

How `dtk` filters `dotnet build` and `dotnet clean` output.

> **How to read these examples.** Every pair below is captured from a real run against the
> projects in `samples/`, and `ExamplesBindingTests` replays each _Raw_ block through the real
> filter on every build, so a page cannot drift from what the tool actually prints. Absolute paths
> are rewritten to `/repo` to keep the captures machine-independent, and a non-zero exit code is
> noted beside the command that produced it.

> **Why the raw output may not look like your terminal.** When `dotnet` writes to a terminal it
> uses the .NET terminal logger, which prints a compact live-updating summary. When its output is
> redirected — which is exactly what `dtk` does — MSBuild falls back to the classic console logger
> shown here, which prints each diagnostic twice: once inline and again in the end-of-build
> summary. That duplication is a large part of what `dtk` removes.

> **Log files:** when the output is too large to display, dtk writes the full output to a log
> file. Pass `--show-log` to print its path.

---

## Success - single project

**Raw** (`dotnet build samples/SampleApp/SampleApp.csproj`)

```sh
  Determining projects to restore...
  Restored /repo/samples/SampleApp/SampleApp.csproj (in 215 ms).
  SampleApp -> /repo/samples/SampleApp/bin/Debug/net10.0/SampleApp.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.19
```

**dtk** (`dtk dotnet build samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet build (1 project, 1.19s)
```

Token reduction: **7 lines -> 1 line**

---

## Success - multiple projects

**Raw** (`dotnet build samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj`)

```sh
  Determining projects to restore...
  Restored /repo/samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj (in 200 ms).
  Restored /repo/samples/SampleApp.Lib/SampleApp.Lib.csproj (in 200 ms).
  SampleApp.Lib -> /repo/samples/SampleApp.Lib/bin/Debug/net10.0/SampleApp.Lib.dll
  SampleApp.MultiProject -> /repo/samples/SampleApp.MultiProject/bin/Debug/net10.0/SampleApp.MultiProject.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.34
```

**dtk** (`dtk dotnet build samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj`)

```sh
✓ dotnet build (2 projects, 1.34s)
```

Token reduction: **9 lines -> 1 line**

---

## Single error

Note how the one error appears twice in the raw output - inline and again under `Build FAILED.` - and once in the dtk summary.

**Raw** (`dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`) — exit code 1

```sh
  Determining projects to restore...
  Restored /repo/samples/SampleApp.Broken/SampleApp.Broken.csproj (in 216 ms).
/repo/samples/SampleApp.Broken/BrokenClass.cs(5,33): error CS0029: Cannot implicitly convert type 'string' to 'int' [/repo/samples/SampleApp.Broken/SampleApp.Broken.csproj]

Build FAILED.

/repo/samples/SampleApp.Broken/BrokenClass.cs(5,33): error CS0029: Cannot implicitly convert type 'string' to 'int' [/repo/samples/SampleApp.Broken/SampleApp.Broken.csproj]
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:01.08
```

**dtk** (`dtk dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`)

```sh
dotnet build: 1 error, 0 warnings (1.08s)
---
samples/SampleApp.Broken/BrokenClass.cs (1 error)
  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

Token reduction: **8 lines -> 5 lines**

---

## Many errors across multiple files

The errors are grouped by file and ranked by frequency, so the file with the most problems is the first thing read. `Top codes` lists the five most frequent codes.

**Raw** (`dotnet build samples/SampleApp.MultiError/SampleApp.MultiError.csproj`) — exit code 1

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
/repo/samples/SampleApp.MultiError/UndefinedReferences.cs(9,27): error CS0103: The name 'undeclaredVariable' does not exist in the current context [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/UndefinedReferences.cs(12,9): error CS0103: The name 'MissingMethod' does not exist in the current context [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(7,32): error CS0029: Cannot implicitly convert type 'string' to 'int' [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(10,37): error CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(9,9): error CS1501: No overload for method 'Add' takes 3 arguments [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(12,13): error CS1503: Argument 1: cannot convert from 'string' to 'int' [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(7,36): error CS0029: Cannot implicitly convert type 'string' to 'bool' [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(10,39): error CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(10,34): error CS0122: 'SecretHolder._value' is inaccessible due to its protection level [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,29): error CS0414: The field 'SecretHolder._value' is assigned but its value is never used [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,5): error S1144: Remove the unused private field '_value'. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,29): error RCS1213: Remove unused field declaration (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1213) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(9,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(6,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/UndefinedReferences.cs(6,17): error S2325: Make 'DoWork' a static method. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(9,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,29): error CA1823: Unused field '_value' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1823) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(6,17): error S2325: Make 'CallWrong' a static method. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(6,17): error CA1822: Member 'CallWrong' does not access instance data and can be marked as static (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1822) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(6,17): error S2325: Make 'TryAccess' a static method. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(6,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(6,17): error CA1822: Member 'TryAccess' does not access instance data and can be marked as static (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1822) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]

Build FAILED.

/repo/samples/SampleApp.MultiError/UndefinedReferences.cs(9,27): error CS0103: The name 'undeclaredVariable' does not exist in the current context [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/UndefinedReferences.cs(12,9): error CS0103: The name 'MissingMethod' does not exist in the current context [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(7,32): error CS0029: Cannot implicitly convert type 'string' to 'int' [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(10,37): error CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(9,9): error CS1501: No overload for method 'Add' takes 3 arguments [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(12,13): error CS1503: Argument 1: cannot convert from 'string' to 'int' [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(7,36): error CS0029: Cannot implicitly convert type 'string' to 'bool' [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(10,39): error CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(10,34): error CS0122: 'SecretHolder._value' is inaccessible due to its protection level [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,29): error CS0414: The field 'SecretHolder._value' is assigned but its value is never used [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,5): error S1144: Remove the unused private field '_value'. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,29): error RCS1213: Remove unused field declaration (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1213) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(9,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/TypeErrors.cs(6,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/UndefinedReferences.cs(6,17): error S2325: Make 'DoWork' a static method. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(9,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(17,29): error CA1823: Unused field '_value' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1823) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(6,17): error S2325: Make 'CallWrong' a static method. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/SignatureErrors.cs(6,17): error CA1822: Member 'CallWrong' does not access instance data and can be marked as static (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1822) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(6,17): error S2325: Make 'TryAccess' a static method. [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/MissingTypes.cs(6,5): error RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
/repo/samples/SampleApp.MultiError/AccessErrors.cs(6,17): error CA1822: Member 'TryAccess' does not access instance data and can be marked as static (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1822) [/repo/samples/SampleApp.MultiError/SampleApp.MultiError.csproj]
    0 Warning(s)
    22 Error(s)

Time Elapsed 00:00:00.94
```

**dtk** (`dtk dotnet build samples/SampleApp.MultiError/SampleApp.MultiError.csproj`)

```sh
dotnet build: 22 errors, 0 warnings (0.94s)
---
samples/SampleApp.MultiError/AccessErrors.cs (7 errors)
  (10,34) CS0122: 'SecretHolder._value' is inaccessible due to its protection level
  (17,29) CS0414: The field 'SecretHolder._value' is assigned but its value is never used
  (17,5) S1144: Remove the unused private field '_value'.
  (17,29) RCS1213: Remove unused field declaration (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1213)
  (17,29) CA1823: Unused field '_value' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1823)
  (6,17) S2325: Make 'TryAccess' a static method.
  (6,17) CA1822: Member 'TryAccess' does not access instance data and can be marked as static (https://learn.microsoft.com/dotnet/fundame...
samples/SampleApp.MultiError/TypeErrors.cs (4 errors)
  (7,32) CS0029: Cannot implicitly convert type 'string' to 'int'
  (10,37) CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)
  (9,5) RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
  (6,5) RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
samples/SampleApp.MultiError/SignatureErrors.cs (4 errors)
  (9,9) CS1501: No overload for method 'Add' takes 3 arguments
  (12,13) CS1503: Argument 1: cannot convert from 'string' to 'int'
  (6,17) S2325: Make 'CallWrong' a static method.
  (6,17) CA1822: Member 'CallWrong' does not access instance data and can be marked as static (https://learn.microsoft.com/dotnet/fundame...
samples/SampleApp.MultiError/MissingTypes.cs (4 errors)
  (7,36) CS0029: Cannot implicitly convert type 'string' to 'bool'
  (10,39) CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?)
  (9,5) RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
  (6,5) RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
samples/SampleApp.MultiError/UndefinedReferences.cs (3 errors)
  (9,27) CS0103: The name 'undeclaredVariable' does not exist in the current context
  (12,9) CS0103: The name 'MissingMethod' does not exist in the current context
  (6,17) S2325: Make 'DoWork' a static method.
Top codes: RCS1181 (4x), S2325 (3x), CS0103 (2x), CS0029 (2x), CS0266 (2x)
```

Token reduction: **50 lines -> 30 lines**

---

## Warnings

Incremental builds skip re-emitting warnings, so `dotnet clean` must precede this run to capture them. Warnings are grouped by code rather than by file: the frequency counts surface the most common pattern first.

**Raw** (`dotnet build samples/SampleApp.Warnings/SampleApp.Warnings.csproj`)

```sh
  Determining projects to restore...
  Restored /repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj (in 209 ms).
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,27): warning CS8600: Converting null literal or possible null value to non-nullable type. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(11,16): warning CS8603: Possible null reference return. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(17,16): warning CS8602: Dereference of a possibly null reference. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(24,16): warning CS8603: Possible null reference return. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(9,13): warning CS0168: The variable 'x' is declared but never used [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning CS0219: The variable 'y' is assigned but its value is never used [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(16,9): warning CS0612: 'ObsoleteUsage.LegacyMethod()' is obsolete [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(17,9): warning CS0618: 'ObsoleteUsage.OldApi()' is obsolete: 'Use NewApi() instead.' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(22,9): warning CS0162: Unreachable code detected [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(6,6): warning S1133: Do not forget to remove this deprecated code someday. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(9,6): warning S1133: Do not forget to remove this deprecated code someday. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(6,6): warning S1123: Add an explanation. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning IDE0007: use 'var' instead of explicit type (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0007) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(6,5): warning RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(14,5): warning RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(15,16): warning S2325: Make 'GetLength' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning IDE0059: Unnecessary assignment of a value to 'unused' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0059) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1124: Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(20,5): warning RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(23,9): warning RCS1124: Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning RCS1124: Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(23,9): warning RCS1118: Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1118: Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,9): warning RCS1118: Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(14,17): warning S2325: Make 'CallDeprecated' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(21,19): warning S2325: Make 'NeverNull' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning S2325: Make 'GetValue' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning S1481: Remove the unused local variable 'unused'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning CA1024: Use properties where appropriate (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1024) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(17,16): warning S2325: Make 'DeadCode' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(6,17): warning S2325: Make 'DoWork' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning IDE0059: Unnecessary assignment of a value to 'y' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0059) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(9,13): warning S1481: Remove the unused local variable 'x'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning S1481: Remove the unused local variable 'y'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
  SampleApp.Warnings -> /repo/samples/SampleApp.Warnings/bin/Debug/net10.0/SampleApp.Warnings.dll

Build succeeded.

/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,27): warning CS8600: Converting null literal or possible null value to non-nullable type. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(11,16): warning CS8603: Possible null reference return. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(17,16): warning CS8602: Dereference of a possibly null reference. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(24,16): warning CS8603: Possible null reference return. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(9,13): warning CS0168: The variable 'x' is declared but never used [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning CS0219: The variable 'y' is assigned but its value is never used [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(16,9): warning CS0612: 'ObsoleteUsage.LegacyMethod()' is obsolete [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(17,9): warning CS0618: 'ObsoleteUsage.OldApi()' is obsolete: 'Use NewApi() instead.' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(22,9): warning CS0162: Unreachable code detected [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(6,6): warning S1133: Do not forget to remove this deprecated code someday. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(9,6): warning S1133: Do not forget to remove this deprecated code someday. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(6,6): warning S1123: Add an explanation. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning IDE0007: use 'var' instead of explicit type (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0007) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(6,5): warning RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(14,5): warning RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(15,16): warning S2325: Make 'GetLength' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning IDE0059: Unnecessary assignment of a value to 'unused' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0059) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1124: Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(20,5): warning RCS1181: Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(23,9): warning RCS1124: Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning RCS1124: Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(23,9): warning RCS1118: Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1118: Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,9): warning RCS1118: Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(14,17): warning S2325: Make 'CallDeprecated' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(21,19): warning S2325: Make 'NeverNull' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning S2325: Make 'GetValue' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning S1481: Remove the unused local variable 'unused'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning CA1024: Use properties where appropriate (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1024) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(17,16): warning S2325: Make 'DeadCode' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(6,17): warning S2325: Make 'DoWork' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning IDE0059: Unnecessary assignment of a value to 'y' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0059) [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(9,13): warning S1481: Remove the unused local variable 'x'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning S1481: Remove the unused local variable 'y'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
    34 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.12
```

**dtk** (`dtk dotnet build samples/SampleApp.Warnings/SampleApp.Warnings.csproj`)

```sh
dotnet build: 0 errors, 34 warnings (1 project, 1.12s)
---
CA1024 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:7 — Use properties where appropriate (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1024)
CS0162 (1x)
  samples/SampleApp.Warnings/UnusedCode.cs:22 — Unreachable code detected
CS0168 (1x)
  samples/SampleApp.Warnings/UnusedCode.cs:9 — The variable 'x' is declared but never used
CS0219 (1x)
  samples/SampleApp.Warnings/UnusedCode.cs:12 — The variable 'y' is assigned but its value is never used
CS0612 (1x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:16 — 'ObsoleteUsage.LegacyMethod()' is obsolete
CS0618 (1x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:17 — 'ObsoleteUsage.OldApi()' is obsolete: 'Use NewApi() instead.'
CS8600 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:10 — Converting null literal or possible null value to non-nullable type.
CS8602 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:17 — Dereference of a possibly null reference.
CS8603 (2x)
  samples/SampleApp.Warnings/NullableWarnings.cs:11 — Possible null reference return.
  samples/SampleApp.Warnings/NullableWarnings.cs:24 — Possible null reference return.
IDE0007 (1x)
  samples/SampleApp.Warnings/NullableWarnings.cs:10 — use 'var' instead of explicit type (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0007)
IDE0059 (2x)
  samples/SampleApp.Warnings/Program.cs:5 — Unnecessary assignment of a value to 'unused' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules...
  samples/SampleApp.Warnings/UnusedCode.cs:12 — Unnecessary assignment of a value to 'y' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0...
RCS1118 (3x)
  samples/SampleApp.Warnings/NullableWarnings.cs:23 — Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118)
  samples/SampleApp.Warnings/NullableWarnings.cs:9 — Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118)
  samples/SampleApp.Warnings/UnusedCode.cs:12 — Mark local variable as const (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1118)
RCS1124 (3x)
  samples/SampleApp.Warnings/NullableWarnings.cs:9 — Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124)
  samples/SampleApp.Warnings/NullableWarnings.cs:23 — Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124)
  samples/SampleApp.Warnings/NullableWarnings.cs:10 — Inline local variable (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1124)
RCS1181 (3x)
  samples/SampleApp.Warnings/NullableWarnings.cs:6 — Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
  samples/SampleApp.Warnings/NullableWarnings.cs:14 — Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
  samples/SampleApp.Warnings/NullableWarnings.cs:20 — Convert comment to documentation comment (https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1181)
S1123 (1x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:6 — Add an explanation.
S1133 (2x)
  samples/SampleApp.Warnings/ObsoleteUsage.cs:6 — Do not forget to remove this deprecated code someday.
  samples/SampleApp.Warnings/ObsoleteUsage.cs:9 — Do not forget to remove this deprecated code someday.
S1481 (3x)
  samples/SampleApp.Warnings/Program.cs:5 — Remove the unused local variable 'unused'.
  samples/SampleApp.Warnings/UnusedCode.cs:9 — Remove the unused local variable 'x'.
  samples/SampleApp.Warnings/UnusedCode.cs:12 — Remove the unused local variable 'y'.
S2325 (6x)
  samples/SampleApp.Warnings/NullableWarnings.cs:15 — Make 'GetLength' a static method.
  samples/SampleApp.Warnings/ObsoleteUsage.cs:14 — Make 'CallDeprecated' a static method.
  samples/SampleApp.Warnings/NullableWarnings.cs:21 — Make 'NeverNull' a static method.
  samples/SampleApp.Warnings/NullableWarnings.cs:7 — Make 'GetValue' a static method.
  samples/SampleApp.Warnings/UnusedCode.cs:17 — Make 'DeadCode' a static method.
  samples/SampleApp.Warnings/UnusedCode.cs:6 — Make 'DoWork' a static method.
```

Token reduction: **75 lines -> 54 lines**

---

## Clean - success

**Raw** (`dotnet clean samples/SampleApp/SampleApp.csproj`)

```sh
Build started 9/12/2026 2:01:37 PM.
     1>Project "/repo/samples/SampleApp/SampleApp.csproj" on node 1 (Clean target(s)).
     1>CoreClean:
         Deleting file "/repo/samples/SampleApp/bin/Debug/net10.0/SampleApp".
         Deleting file "/repo/samples/SampleApp/bin/Debug/net10.0/SampleApp.deps.json".
         Deleting file "/repo/samples/SampleApp/bin/Debug/net10.0/SampleApp.runtimeconfig.json".
         Deleting file "/repo/samples/SampleApp/bin/Debug/net10.0/SampleApp.dll".
         Deleting file "/repo/samples/SampleApp/bin/Debug/net10.0/SampleApp.pdb".
         Deleting file "/repo/samples/SampleApp/bin/Debug/net10.0/SampleApp.xml".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.GeneratedMSBuildEditorConfig.editorconfig".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.AssemblyInfoInputs.cache".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.AssemblyInfo.cs".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.csproj.CoreCompileInputs.cache".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.sourcelink.json".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.dll".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/refint/SampleApp.dll".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.xml".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.pdb".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/SampleApp.genruntimeconfig.cache".
         Deleting file "/repo/samples/SampleApp/obj/Debug/net10.0/ref/SampleApp.dll".
     1>Done Building Project "/repo/samples/SampleApp/SampleApp.csproj" (Clean target(s)).

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.34
```

**dtk** (`dtk dotnet clean samples/SampleApp/SampleApp.csproj`)

```sh
✓ dotnet clean
```

Token reduction: **25 lines -> 1 line**
