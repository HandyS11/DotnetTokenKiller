# Build & Clean Examples

How `dtk` filters `dotnet build` and `dotnet clean` output.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is what you would send to your LLM.
> **Log files:** when the output is too large to display, DTK writes the full output to a log file. Pass `--show-log` to print its path.

---

## Success — Single Project

**Raw** (`dotnet build samples/SampleApp/SampleApp.csproj`)

```text
Restore complete (0.6s)
  SampleApp net10.0 succeeded (0.4s) → samples\SampleApp\bin\Debug\net10.0\SampleApp.dll
Build succeeded in 1.9s
```

**dtk** (`dtk dotnet build samples/SampleApp/SampleApp.csproj`)

```text
✓ dotnet build (1 project, 1.86s)
```

Token reduction: **3 lines → 1 line**

---

## Success — Multiple Projects

**Raw** (`dotnet build samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj`)

```text
Restore complete (0.8s)
  SampleApp.Lib net10.0 succeeded (0.6s) → samples\SampleApp.Lib\bin\Debug\net10.0\SampleApp.Lib.dll
  SampleApp.MultiProject net10.0 succeeded (0.8s) → samples\SampleApp.MultiProject\bin\Debug\net10.0\SampleApp.MultiProject.dll
Build succeeded in 3.6s
```

**dtk** (`dtk dotnet build samples/SampleApp.MultiProject/SampleApp.MultiProject.csproj`)

```text
✓ dotnet build (2 projects, 2.78s)
```

Token reduction: **4 lines → 1 line**

---

## Single Error

**Raw** (`dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`)

```text
Restore complete (1.1s)
  SampleApp.Broken net10.0 failed with 1 error(s) (1.0s)
    D:\DotnetTokenKiller\samples\SampleApp.Broken\BrokenClass.cs(5,33): error CS0029: Cannot implicitly convert type 'string' to 'int'
Build failed with 1 error(s) in 3.2s
```

**dtk** (`dtk dotnet build samples/SampleApp.Broken/SampleApp.Broken.csproj`)

```text
dotnet build: 1 error, 0 warnings
---
samples/SampleApp.Broken/BrokenClass.cs (1 error)
  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

Key improvements:

- Absolute paths replaced with workspace-relative paths
- Error grouped by file with location
- Top error codes summarized

---

## Many Errors Across Multiple Files

**Raw** (`dotnet build samples/SampleApp.MultiError/SampleApp.MultiError.csproj`)

> The raw output is ~25 lines with full absolute paths for each error.

**dtk** (`dtk dotnet build samples/SampleApp.MultiError/SampleApp.MultiError.csproj`)

```text
dotnet build: 25 errors, 0 warnings
---
samples/SampleApp.MultiError/SignatureErrors.cs (3 errors)
  (9,9) CS1501: No overload for method 'Add' takes 3 arguments
  (12,13) CS1503: Argument 1: cannot convert from 'string' to 'int'
  (9,9) RCS1181: Convert comment to documentation comment
samples/SampleApp.MultiError/MissingTypes.cs (3 errors)
  (7,36) CS0029: Cannot implicitly convert type 'string' to 'bool'
  (10,39) CS0266: Cannot implicitly convert type 'long' to 'int'. ...
  (7,1) RCS1181: Convert comment to documentation comment
samples/SampleApp.MultiError/TypeErrors.cs (3 errors)
  ...
samples/SampleApp.MultiError/AccessErrors.cs (6 errors)
  ...
samples/SampleApp.MultiError/UndefinedReferences.cs (3 errors)
  ...
Top codes: CS0122 (5x), RCS1181 (4x), CS0029 (2x), CS0266 (2x), ...
```

Key improvements:

- Errors grouped by file
- Each file shows its error count
- Top error codes aggregated across all files
- Absolute paths replaced with workspace-relative paths
