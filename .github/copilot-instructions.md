<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, pack, tool restore, ef migrations, ef database, stryker, and jb cleanupcode commands.

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
dtk dotnet tool restore
dtk dotnet ef migrations add <Name>
dtk dotnet stryker
dtk dotnet jb cleanupcode <solution>
```

- Arguments and flags are forwarded unchanged; `dtk test`, `dtk build` etc. work without `dotnet`.
- Exit codes are preserved.
- Unknown subcommands (e.g. `run`, `watch`) and `--format json`/`-getProperty:` output pass through unchanged.
- Don't pipe dtk output into tail or grep: its last line is the status.
- `dtk log [build|test]` prints the latest run's raw output instead of re-running: last 100 lines, `--full` for all, `--list`.
- `dtk pipe <subcommand>` filters stdin. `-q` on dotnet's own subcommands prints only the filtered output.
- Options on filtered runs: `--tee <path>` keeps the raw output as evidence, `--max-lines N` caps the body; `test` also takes `--names` (passed tests), `--show-output` (failing tests' output) and `--fail-on-empty` (exit 8 if no test ran).
- Raw output: `DTK_RAW=1` (environment or prefix); a hook that rewrites plain `dotnet …` skips `\dotnet …`, `command dotnet …` and `rtk proxy dotnet …`.

A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
`dotnet build|test|restore|clean|format|list package|publish|pack|tool restore|ef migrations|ef database|stryker|jb cleanupcode` to `dtk dotnet ...` automatically.
<!-- /dtk -->
