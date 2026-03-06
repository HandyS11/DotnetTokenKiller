# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

DotnetTokenKiller is a .NET CLI proxy that reduces LLM token usage through dotnet commands.

## Commands

```bash
# Build
dotnet build DotnetTokenKiller.slnx

# Run tests
dotnet test DotnetTokenKiller.slnx

# Run a single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Format code
dotnet format DotnetTokenKiller.slnx --no-restore

# Verify formatting without making changes
dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

## Git Hooks

The pre-commit hook auto-formats staged `.cs` files and validates `.csproj`/`.props` files. Install it once with:

```bash
git config core.hooksPath .githooks
```

## Architecture & Stack

- **Target framework**: net10.0
- **CLI framework**: Spectre.Console + Spectre.Console.Cli
- **Testing**: xunit + FluentAssertions + Spectre.Console.Testing
- **Package management**: Central via `Directory.Packages.props` — all version numbers go there, `.csproj` files omit versions

## Code Style

Enforced via `.editorconfig` and build-time analyzers (Roslynator, SonarAnalyzer, Microsoft.CodeAnalysis.NetAnalyzers):

- `TreatWarningsAsErrors` is enabled — all analyzer warnings must be resolved
- File-scoped namespaces (`namespace Foo;`)
- `var` preferred throughout
- Private fields: `_camelCase`; async methods must end in `Async`
- Interfaces: `IPascalCase`; type parameters: `TPascalCase`
- Line endings: LF only; no trailing whitespace; no BOM; 4-space indent for `.cs`, 2-space for XML/JSON/YAML
