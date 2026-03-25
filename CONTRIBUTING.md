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

# Restore dependencies
dotnet restore DotnetTokenKiller.slnx

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
dotnet format DotnetTokenKiller.slnx --no-restore
```

To verify without making changes:

```sh
dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

## Package Management

Package versions are managed centrally in `Directory.Packages.props`. Add version numbers there; `.csproj` files reference packages without version attributes.

## Pull Request Process

1. Fork the repository and create a feature branch from `develop`.
2. Make your changes with tests covering the new behaviour.
3. Ensure `dtk dotnet build` and `dtk dotnet test` both pass with no warnings.
4. Open a pull request against the `develop` branch with a clear description of the change and the motivation behind it.

## Reporting Issues

Please use the [GitHub issue tracker](https://github.com/HandyS11/DotnetTokenKiller/issues) to report bugs or request features. Use the provided templates where applicable.
