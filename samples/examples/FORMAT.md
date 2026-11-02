# Format Examples

How `dtk` filters `dotnet format` output.

> **How to read these examples.** Every pair below is captured from a real run against the
> projects in `samples/`, and `ExamplesBindingTests` replays each _Raw_ block through the real
> filter on every build, so a page cannot drift from what the tool actually prints. Absolute paths
> are rewritten to `/repo` to keep the captures machine-independent, and a non-zero exit code is
> noted beside the command that produced it.

---

## Nothing to format

`dotnet format` says nothing at all when there is nothing to change, which is indistinguishable from a crash. dtk synthesises the success signal the raw tool omits.

**Raw** (`dotnet format samples/SampleApp/SampleApp.csproj --verify-no-changes`)

```sh

```

**dtk** (`dtk dotnet format samples/SampleApp/SampleApp.csproj --verify-no-changes`)

```sh
✓ dotnet format (nothing to format)
```

Token reduction: **0 lines -> 1 line**

---

## Verify-no-changes, violations found

Violations are grouped by file and code, one line each, led by the file's workspace-relative path: `file: CODE ×n (line:col, …)`, with `×n` only for a group of more than one and every position in line and column order. A message that is the same for the whole group follows once; violations of one code with different messages (S2325 names the method) keep a line each. The `[project.csproj]` suffix goes, and a diagnostic the tool reports twice counts once, which is why 33 raw lines are 30 violations. The files with the most violations come first, ties in name order, and nothing is cut off: the last line is the verdict, `✗ dotnet format: 30 violations in 4 files` (`⚠` instead of `✗` when the exit code is zero). The sample projects hold no whitespace violations; how those read is shown under `dtk dotnet format` in the usage guide.

**Raw** (`dotnet format samples/SampleApp.Warnings/SampleApp.Warnings.csproj --verify-no-changes`) — exit code 2

```sh
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning IDE0007: use 'var' instead of explicit type [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning IDE0059: Unnecessary assignment of a value to 'y' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning IDE0059: Unnecessary assignment of a value to 'unused' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning IDE0007: use 'var' instead of explicit type [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning CA1024: Use properties where appropriate [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(6,5): warning RCS1181: Convert comment to documentation comment [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(20,5): warning RCS1181: Convert comment to documentation comment [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(14,5): warning RCS1181: Convert comment to documentation comment [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1124: Inline local variable [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning RCS1124: Inline local variable [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(23,9): warning RCS1124: Inline local variable [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1118: Mark local variable as const [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(23,9): warning RCS1118: Mark local variable as const [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning S2325: Make 'GetValue' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(21,19): warning S2325: Make 'NeverNull' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(15,16): warning S2325: Make 'GetLength' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning IDE0059: Unnecessary assignment of a value to 'y' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,9): warning RCS1118: Mark local variable as const [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(17,16): warning S2325: Make 'DeadCode' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(6,17): warning S2325: Make 'DoWork' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(9,13): warning S1481: Remove the unused local variable 'x'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning S1481: Remove the unused local variable 'y'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning IDE0059: Unnecessary assignment of a value to 'unused' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/Program.cs(5,5): warning S1481: Remove the unused local variable 'unused'. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(14,17): warning S2325: Make 'CallDeprecated' a static method. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(9,6): warning S1133: Do not forget to remove this deprecated code someday. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(6,6): warning S1133: Do not forget to remove this deprecated code someday. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(6,6): warning S1123: Add an explanation. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(10,27): warning CS8600: Converting null literal or possible null value to non-nullable type. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(11,16): warning CS8603: Possible null reference return. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(16,9): warning CS0612: 'ObsoleteUsage.LegacyMethod()' is obsolete [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/ObsoleteUsage.cs(17,9): warning CS0618: 'ObsoleteUsage.OldApi()' is obsolete: 'Use NewApi() instead.' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
/repo/samples/SampleApp.Warnings/NullableWarnings.cs(24,16): warning CS8603: Possible null reference return. [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
```

**dtk** (`dtk dotnet format samples/SampleApp.Warnings/SampleApp.Warnings.csproj --verify-no-changes`)

```sh
samples/SampleApp.Warnings/NullableWarnings.cs: RCS1124 ×3 (9:9, 10:9, 23:9): Inline local variable
samples/SampleApp.Warnings/NullableWarnings.cs: RCS1181 ×3 (6:5, 14:5, 20:5): Convert comment to documentation comment
samples/SampleApp.Warnings/NullableWarnings.cs: CS8603 ×2 (11:16, 24:16): Possible null reference return.
samples/SampleApp.Warnings/NullableWarnings.cs: RCS1118 ×2 (9:9, 23:9): Mark local variable as const
samples/SampleApp.Warnings/NullableWarnings.cs: CA1024 (7:19): Use properties where appropriate
samples/SampleApp.Warnings/NullableWarnings.cs: CS8600 (10:27): Converting null literal or possible null value to non-nullable type.
samples/SampleApp.Warnings/NullableWarnings.cs: IDE0007 (10:9): use 'var' instead of explicit type
samples/SampleApp.Warnings/NullableWarnings.cs: S2325 (15:16): Make 'GetLength' a static method.
samples/SampleApp.Warnings/NullableWarnings.cs: S2325 (7:19): Make 'GetValue' a static method.
samples/SampleApp.Warnings/NullableWarnings.cs: S2325 (21:19): Make 'NeverNull' a static method.
samples/SampleApp.Warnings/ObsoleteUsage.cs: S1133 ×2 (6:6, 9:6): Do not forget to remove this deprecated code someday.
samples/SampleApp.Warnings/ObsoleteUsage.cs: CS0612 (16:9): 'ObsoleteUsage.LegacyMethod()' is obsolete
samples/SampleApp.Warnings/ObsoleteUsage.cs: CS0618 (17:9): 'ObsoleteUsage.OldApi()' is obsolete: 'Use NewApi() instead.'
samples/SampleApp.Warnings/ObsoleteUsage.cs: S1123 (6:6): Add an explanation.
samples/SampleApp.Warnings/ObsoleteUsage.cs: S2325 (14:17): Make 'CallDeprecated' a static method.
samples/SampleApp.Warnings/UnusedCode.cs: IDE0059 (12:13): Unnecessary assignment of a value to 'y'
samples/SampleApp.Warnings/UnusedCode.cs: RCS1118 (12:9): Mark local variable as const
samples/SampleApp.Warnings/UnusedCode.cs: S1481 (9:13): Remove the unused local variable 'x'.
samples/SampleApp.Warnings/UnusedCode.cs: S1481 (12:13): Remove the unused local variable 'y'.
samples/SampleApp.Warnings/UnusedCode.cs: S2325 (17:16): Make 'DeadCode' a static method.
samples/SampleApp.Warnings/UnusedCode.cs: S2325 (6:17): Make 'DoWork' a static method.
samples/SampleApp.Warnings/Program.cs: IDE0059 (5:5): Unnecessary assignment of a value to 'unused'
samples/SampleApp.Warnings/Program.cs: S1481 (5:5): Remove the unused local variable 'unused'.
✗ dotnet format: 30 violations in 4 files
```

Token reduction: **33 lines -> 24 lines**
