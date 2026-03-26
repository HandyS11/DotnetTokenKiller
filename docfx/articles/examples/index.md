# Output Examples

These pages show side-by-side comparisons of raw `dotnet` output versus the filtered DTK output, so you can see exactly what gets stripped.

> **How to read these examples:** the _Raw_ block is what `dotnet` actually prints to stdout; the _dtk_ block is the compact output you would send to your LLM.

## Commands

- [Build & Clean](build.md) — single project, multi-project, errors, warnings
- [Test](test.md) — xUnit single failure, multi-failure with FluentAssertions
- [Restore](restore.md) — missing package errors
- [Format](format.md) — nothing to format, verify-no-changes violations

## Typical Token Savings

| Command | Typical Savings |
|---------|----------------|
| build success | 3 lines → 1 line |
| build errors (25) | ~20 lines → grouped summary |
| test (single failure) | ~35 lines → 5 lines |
| test (12 failures) | ~250 lines → compact summary |
| clean | ~98% reduction |
| restore errors | Absolute paths → relative paths, cleaner formatting |
| format violations | Absolute paths + noise → relative paths only |
