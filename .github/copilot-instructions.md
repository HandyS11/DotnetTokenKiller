<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.

```sh
dtk dotnet build MyProject.slnx
dtk dotnet test --filter "Category=Unit"
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet format --verify-no-changes
dtk dotnet list package --outdated
```

- All arguments and flags are forwarded to `dotnet` unchanged.
- Exit codes are preserved — CI pipelines work correctly.
- Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.

A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
`dotnet build|test|restore|clean|format|list package` to `dtk dotnet ...` automatically.
<!-- /dtk -->
