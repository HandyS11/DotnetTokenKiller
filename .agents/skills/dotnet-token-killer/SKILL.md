---
name: dotnet-token-killer
description: 'Use `dtk` (DotnetTokenKiller) instead of raw `dotnet` commands to reduce token usage when building, testing, restoring, or cleaning .NET projects.'
---

# DotnetTokenKiller (dtk)

`dtk` wraps `dotnet` commands and filters output to actionable signal only, saving 50-97% of tokens by stripping SDK banners, MSBuild noise, progress lines, and duplicate diagnostics.

## Installation

```sh
dotnet tool install -g DotnetTokenKiller  # requires .NET 10 SDK
```

## Usage

Drop-in replacement for `dotnet build`, `test`, `restore`, `clean`, and `format`. All arguments and flags are forwarded unchanged:

```sh
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test --filter "Category=Unit"
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet format --verify-no-changes
```

Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.

## Flags

| Flag         | Purpose                                           |
|--------------|---------------------------------------------------|
| `--show-log` | Print path to full unfiltered log after a run     |
| `-v`         | Increase verbosity (repeatable: `-v -v`)          |

## Key Behaviors

- Paths are workspace-relative (`src/Foo.cs`, not absolute)
- Build errors grouped by file; warnings grouped by diagnostic code with frequency counts
- Exit codes preserved — CI pipelines work correctly
- Works with xUnit, NUnit, MSTest, and Reqnroll
- Run `dtk dotnet clean` first for a full warning report (incremental builds skip unchanged files)

## Token Savings

```sh
dtk gain               # last 30 days
dtk gain --days 7
dtk gain --project     # current project only
dtk gain --json
```
