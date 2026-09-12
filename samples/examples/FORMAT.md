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

Each violation loses its absolute path and its repeated `[project.csproj]` suffix.

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
dotnet format: 33 violations
samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning IDE0007: use 'var' instead of explicit type [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/UnusedCode.cs(12,13): warning IDE0059: Unnecessary assignment of a value to 'y' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/Program.cs(5,5): warning IDE0059: Unnecessary assignment of a value to 'unused' [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning IDE0007: use 'var' instead of explicit type [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(7,19): warning CA1024: Use properties where appropriate [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(6,5): warning RCS1181: Convert comment to documentation comment [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(20,5): warning RCS1181: Convert comment to documentation comment [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(14,5): warning RCS1181: Convert comment to documentation comment [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(9,9): warning RCS1124: Inline local variable [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
samples/SampleApp.Warnings/NullableWarnings.cs(10,9): warning RCS1124: Inline local variable [/repo/samples/SampleApp.Warnings/SampleApp.Warnings.csproj]
... and 23 more violations
```

Token reduction: **33 lines -> 12 lines**
