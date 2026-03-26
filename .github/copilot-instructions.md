<!-- dtk -->

## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.

```sh
dtk dotnet build MyProject.slnx
dtk dotnet test --filter "Category=Unit"
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet format --verify-no-changes
```

- All arguments and flags are forwarded to `dotnet` unchanged.
- Exit codes are preserved — CI pipelines work correctly.
- Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.

<!-- /dtk -->
