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

- Run them plainly: dtk's output is already short and its last line is the verdict. Don't pipe it into `tail` or `grep`; `--max-lines N` caps it.
- To see more of a run, or what a cap cut, use `dtk log build` / `dtk log test` (`--full` for all) instead of running it again.
- `dtk dotnet test --names` lists the tests that passed; `DTK_RAW=1` gives dotnet's own output.
- Arguments and flags are forwarded unchanged; `dtk test`, `dtk build` etc. work without `dotnet`.
- Exit codes are preserved.
- Unknown subcommands (e.g. `run`, `watch`) and `--format json`/`-getProperty:` output pass through unchanged.
- `dtk pipe <subcommand>` filters stdin. `-q` (dotnet subcommands) prints only the filtered output.
- `--tee <file>` keeps raw output as evidence; for test: `--show-output`, `--fail-on-empty`.
- Raw output: `DTK_RAW=1` (environment or prefix); a hook that rewrites plain `dotnet …` skips `\dotnet …`, `command dotnet …` and `rtk proxy dotnet …`.

A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
`dotnet build|test|restore|clean|format|list package|publish|pack|tool restore|ef migrations|ef database|stryker|jb cleanupcode` to `dtk dotnet ...` automatically.
<!-- /dtk -->
