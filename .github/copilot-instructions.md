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

A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` rewrites `dotnet build|test|restore|clean|format|list package`
to `dtk dotnet ...` automatically. The hook shells out to `python3`; on Windows (where the launcher is
usually `python`, not `python3`), edit the `bash` command in that file if it doesn't fire.
<!-- /dtk -->