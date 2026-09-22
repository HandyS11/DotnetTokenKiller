<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.

```sh
dtk dotnet build MyProject.slnx
dtk dotnet test --filter "Category=Unit"
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet format --verify-no-changes
dtk dotnet list package --outdated
dtk dotnet publish -c Release
dtk dotnet pack
```

- All arguments and flags are forwarded to `dotnet` unchanged.
- Exit codes are preserved — CI pipelines work correctly.
- Unknown subcommands (e.g. `run`, `watch`) pass through to `dotnet` unchanged.

A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
`dotnet build|test|restore|clean|format|list package|publish|pack` to `dtk dotnet ...` automatically.
<!-- /dtk -->
