# Contributing to DotnetTokenKiller

Thank you for your interest in contributing!

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later (full SDK, not just the runtime)
- Git

## Development Setup

```sh
git clone https://github.com/HandyS11/DotnetTokenKiller.git
cd DotnetTokenKiller

# Install the Git hooks (auto-formats staged .cs files and validates .csproj/.props files on commit)
git config core.hooksPath .githooks

# Install local .NET tools (includes dtk itself)
dotnet tool restore

# Restore dependencies
dtk dotnet restore DotnetTokenKiller.slnx

# Build (use dtk for reduced output)
dtk dotnet build DotnetTokenKiller.slnx

# Run tests
dtk dotnet test DotnetTokenKiller.slnx
```

## Running Tests

```sh
# All tests
dtk dotnet test DotnetTokenKiller.slnx

# Single test
dtk dotnet test --filter "FullyQualifiedName~ClassName.MethodName"
```

## Code Style

Code style is enforced via `.editorconfig` and build-time analyzers. The project uses `TreatWarningsAsErrors`, so all analyzer warnings must be resolved before a build passes.

To format your code before committing:

```sh
dtk dotnet format DotnetTokenKiller.slnx --no-restore
```

To verify without making changes:

```sh
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

## Package Management

Package versions are managed centrally in `Directory.Packages.props`. Add version numbers there; `.csproj` files reference packages without version attributes.

## How to Add a New Filter

To add filtering support for a new `dotnet` subcommand (e.g., `dotnet publish`):

1. **Create the filter** — add a new class in `src/DotnetTokenKiller.Application/Filters/` implementing `IOutputFilter`. Follow the existing filters (`DotnetBuildFilter`, `DotnetTestFilter`, etc.) as a template.
2. **Register the filter** — add a keyed registration in `src/DotnetTokenKiller.Application/DependencyInjection.cs`.
3. **Add a CLI command** — create a new command class in `src/DotnetTokenKiller.Cli/Commands/` following the existing pattern (e.g., `DotnetBuildCommand`). Wire it up in `Program.cs`.
4. **Add tests** — create a test class in `tests/DotnetTokenKiller.Application.Tests/Filters/` with representative input/output scenarios. Use embedded resources for large test fixtures.
5. **Add a sample project** (optional) — if the new command benefits from a reproducible fixture, add one under `samples/`.
6. **Update documentation** — add the new command to the Usage Guide, Architecture filters table, and Output Examples.

## Pull Request Process

1. Fork the repository and create a feature branch from `develop`.
2. Make your changes with tests covering the new behaviour.
3. Ensure `dtk dotnet build` and `dtk dotnet test` both pass with no warnings.
4. Open a pull request against the `develop` branch with a clear description of the change and the motivation behind it.

## Reporting Issues

Please use the [GitHub issue tracker](https://github.com/HandyS11/DotnetTokenKiller/issues) to report bugs or request features. Use the provided templates where applicable.
