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
