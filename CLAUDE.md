# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

DotnetTokenKiller is a .NET CLI proxy that reduces LLM token usage through dotnet commands.

## Commands

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package to reduce token usage. A
PreToolUse hook in
`.claude/settings.json` automatically rewrites these commands.

```bash
# Build
dtk dotnet build DotnetTokenKiller.slnx

# Run tests
dtk dotnet test DotnetTokenKiller.slnx

# Run a single test
dtk dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Format code
dtk dotnet format DotnetTokenKiller.slnx --no-restore

# Verify formatting without making changes
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes

# Inspect package references with filtered output
dtk dotnet list package --outdated

# Run the benchmark suite (Release only; the full run takes tens of minutes)
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*FilterBenchmarks*'

# Measure the end-to-end cold-start cost of the built binary
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start

# Measure the one-time tiktoken vocabulary load (one fresh process per sample, ~10s)
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- tokenizer-load

# Regenerate the savings baseline after intentionally changing a filter
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline

# Inspect code quality with ReSharper CLT (jb is a local dotnet tool)
jb inspectcode DotnetTokenKiller.slnx --output=artifacts/inspectcode.xml --format=Xml

# Apply ReSharper cleanup (reformat + syntax style) — run after build
jb cleanupcode DotnetTokenKiller.slnx --profile="Built-in: Reformat & Apply Syntax Style"
```

`dtk integrate copilot-cli` installs a GitHub Copilot CLI `preToolUse` hook (`.github/hooks/`) that rewrites
`dotnet …` to `dtk dotnet …`. Supports `--global` (`~/.copilot/hooks/`). Distinct from `dtk integrate copilot`
(instruction-only, Copilot IDE).

## Git Hooks

The pre-commit hook auto-formats staged `.cs` files and validates `.csproj`/`.props` files. Install it once with:

```bash
git config core.hooksPath .githooks
```

## Benchmarks

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus` holds the fixture corpus, a seeded log generator
and the savings engine; `benchmarks/DotnetTokenKiller.Benchmarks` holds the BenchmarkDotNet suite.

Performance here has two dimensions, gated differently:

- **Token savings** is deterministic and hard-gated. `SavingsBaselineTests` compares every scenario
  against `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` and runs
  as part of `dotnet test`. **Changing a filter's output changes its savings and fails this test.**
  That is intended: regenerate with `update-baseline` and let the diff show how the numbers moved.
- **Timings** are never gated. Shared CI runners vary too much for a threshold to mean anything, so
  the suite runs on demand via the `Benchmarks` workflow and uploads its results as artifacts.

Two costs cannot be measured in process and have their own verbs instead of BenchmarkDotNet jobs:

- `cold-start` times the built `dtk` binary end to end (`dtk pipe build` with a fixture on stdin),
  55 spawns, median and p95.
- `tokenizer-load` times the one-time tiktoken vocabulary load, **one fresh process per sample**.
  `Microsoft.ML.Tokenizers` caches the parsed vocabulary in internal static state, so an
  in-process benchmark measures a cache hit — microseconds for something that costs about 113 ms.
  Do not "simplify" this back into a `[Benchmark]`; there is no in-process form of it that is not
  a lie. Measured 2026-09-12: `cl100k_base` median 112.7 ms, `o200k_base` median 173.6 ms, against
  a 287.9 ms cold-start median on the same machine.

Both fail loudly — non-zero exit, the child's own output — rather than reporting a fast number they
did not measure. A BenchmarkDotNet run that matches no benchmark also exits non-zero, so a typo in
the workflow's `filter` input cannot go green with an empty artifact.

## Architecture & Stack

- **Target framework**: net10.0
- **CLI framework**: Spectre.Console + Spectre.Console.Cli
- **Testing**: xunit + FluentAssertions + Spectre.Console.Testing
- **Package management**: Central via `Directory.Packages.props` — all version numbers go there, `.csproj` files omit
  versions

## Code Style

Enforced via `.editorconfig` and build-time analyzers (Roslynator, SonarAnalyzer, Microsoft.CodeAnalysis.NetAnalyzers):

- `TreatWarningsAsErrors` is enabled — all analyzer warnings must be resolved
- File-scoped namespaces (`namespace Foo;`)
- `var` preferred throughout
- Private fields: `_camelCase`; async methods must end in `Async`
- Interfaces: `IPascalCase`; type parameters: `TPascalCase`
- Line endings: LF only; no trailing whitespace; no BOM; 4-space indent for `.cs`, 2-space for XML/JSON/YAML
