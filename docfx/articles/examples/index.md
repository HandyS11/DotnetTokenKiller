# Output Examples

These pages show side-by-side comparisons of raw `dotnet` output versus the filtered DTK output, so you can see exactly what gets stripped.

> **How to read these examples:** every pair is captured from a real run against the projects in
> `samples/`, and `ExamplesBindingTests` replays each _Raw_ block through the real filter on every
> build, so these pages cannot drift from what the tool actually prints. Absolute paths are
> rewritten to `/repo` to keep the captures machine-independent.

> **Note:** the _Raw_ blocks show what `dotnet` writes to a **redirected** stdout, which is what
> `dtk` receives. In an interactive terminal .NET uses the terminal logger instead, so your screen
> looks more compact than the raw blocks here.

## Commands

- [Build & Clean](build.md) — single project, multi-project, errors, warnings
- [Test](test.md) — xUnit, NUnit, MSTest and Reqnroll failures
- [Restore](restore.md) — missing package errors
- [Format](format.md) — nothing to format, verify-no-changes violations

## Measured Token Savings

Line counts below are the ones rendered on each page, counting non-blank raw lines.

| Command | Raw | dtk |
|---------|-----|-----|
| build success (1 project) | 7 lines | 1 line |
| build success (2 projects) | 9 lines | 1 line |
| build, single error | 8 lines | 5 lines |
| build, 22 errors across 5 files | 50 lines | 30 lines |
| build, 34 warnings | 75 lines | 54 lines |
| clean | 25 lines | 1 line |
| test, 1 failure (xUnit) | 14 lines | 5 lines |
| test, 12 failures (xUnit) | 118 lines | 38 lines |
| test, 3 failures (NUnit) | 30 lines | 11 lines |
| test, 3 failures (MSTest) | 27 lines | 11 lines |
| test, 2 failures (Reqnroll) | 64 lines | 8 lines |
| restore, missing package | 3 lines | 2 lines |
| format, 33 violations | 33 lines | 12 lines |
