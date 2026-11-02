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

- All arguments and flags are forwarded to `dotnet` unchanged. `dtk test`, `dtk build` and the other subcommands also work without `dotnet`.
- Exit codes are preserved — CI pipelines work correctly.
- Unknown subcommands (e.g. `run`, `watch`), machine-readable runs (`list package --format json`, `-getProperty:`) and `--interactive` pass through to `dotnet` unchanged.
- Read the whole output. Don't pipe dtk output into tail or grep: its last line is the status.
- `dtk log` prints the raw output of the latest run, passing or failing (last 100 lines, `--full` for all): use it instead of re-running. `dtk log build` or `dtk log test` picks a kind, `--list` shows what is kept.
- `dtk pipe <subcommand>` filters output read from stdin; `-q` prints the filtered output alone.
- Raw `dotnet` output: set `DTK_RAW=1` (in the environment or as a prefix). Where dtk's hook rewrites plain `dotnet …` commands, it leaves `\dotnet …`, `command dotnet …` and `rtk proxy dotnet …` alone.

A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
`dotnet build|test|restore|clean|format|list package|publish|pack` to `dtk dotnet ...` automatically.
<!-- /dtk -->
