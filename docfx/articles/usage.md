# Usage Guide

## General Syntax

```sh
dtk [OPTIONS] <COMMAND>
```

### Global Options

| Option | Description |
|--------|-------------|
| `-h`, `--help` | Print help information |
| `-v`, `--version` | Print version information |

## Commands

### `dtk dotnet build`

Run `dotnet build` with filtered output. All positional arguments and flags are forwarded:

```sh
dtk dotnet build
dtk dotnet build --configuration Release
dtk dotnet build src/MyProject/MyProject.csproj
dtk dotnet build --no-restore
dtk dotnet build -q                  # quiet: filtered content only, no DTK meta-output
```

On success, output is reduced to a single summary line. On failure, errors are grouped by file with workspace-relative paths and top error codes.

### `dtk dotnet test`

Run `dotnet test` with filtered output:

```sh
dtk dotnet test
dtk dotnet test --filter "Category=Unit"
dtk dotnet test --configuration Release
dtk dotnet test src/MyProject.Tests/MyProject.Tests.csproj
dtk dotnet test -q                   # quiet mode
```

Passing tests are summarized with counts. Failing tests show the test name, duration, error message, and a clean stack trace with relative paths. xUnit/NUnit/MSTest adapter banners, license warnings, and framework internals are stripped.

### `dtk dotnet restore`

Run `dotnet restore` with filtered output:

```sh
dtk dotnet restore
dtk dotnet restore src/MyProject/MyProject.csproj
dtk dotnet restore -q
```

Restore errors include the error code, message, and workspace-relative project path.

### `dtk dotnet clean`

Run `dotnet clean` with filtered output:

```sh
dtk dotnet clean
dtk dotnet clean --configuration Release
dtk dotnet clean -q
```

### `dtk dotnet format`

Run `dotnet format` with filtered output:

```sh
dtk dotnet format
dtk dotnet format MyApp.slnx
dtk dotnet format --verify-no-changes
dtk dotnet format -q
```

When nothing needs formatting, the raw command produces no output at all. dtk synthesises a `✓ dotnet format (nothing to format)` confirmation so AI agents receive an explicit positive signal. When `--verify-no-changes` finds violations, the file paths and violation types are shown with workspace-relative paths.

### `dtk dotnet list package`

Run `dotnet list package` with filtered output, covering all four of its variants:

```sh
dtk dotnet list package
dtk dotnet list package --outdated
dtk dotnet list package --deprecated
dtk dotnet list package --vulnerable
```

The plain variant collapses packages shared by every project into one `all projects:` line, then
lists only each project's remaining additions. The `--outdated`, `--deprecated`, and `--vulnerable`
variants group findings by package across projects instead of repeating them per project, and a
clean audit run (no findings) collapses to a single `✓` line. Output is capped at 30 package groups,
with an explicit truncation line if there are more.

If the output arrives in a shape dtk does not recognise — `--format json`, a localised SDK, or a
future column layout — dtk prints it unfiltered behind a
`⚠ dotnet list package: unrecognized output, passed through unfiltered` line rather than guessing.
`dotnet list package` always exits 0, even when it reports vulnerable packages, so this is the only
signal available: a clean `✓` from dtk always means dtk read the tables, never that it failed to.

#### Which spellings are filtered

Only the two-tokens-adjacent form is filtered. These are:

```sh
dtk dotnet list package                 # filtered
dtk dotnet list package --outdated      # filtered
dtk dotnet list package MyApp.sln       # filtered (target after the two tokens)
```

These pass through to `dotnet` **unfiltered**, with the raw output shown as-is:

```sh
dtk dotnet list MyApp.sln package       # NOT filtered — target between the two tokens
dtk dotnet package list                 # NOT filtered — noun-first spelling
```

`dotnet list <PROJECT|SOLUTION|FILE> package` is the SDK's primary documented synopsis, and
`dotnet package list` exists in .NET 10, so both are legitimate spellings — dtk simply does not
match them yet, because its subcommand matcher requires the tokens to be adjacent at the front of
the argument list. Passthrough is safe (the output is correct, just unfiltered) and these runs are
recorded under `list` and `package` respectively in `dtk gain --coverage`. The generated agent hooks
do not rewrite them either.

### `dtk pipe`

Filter output on stdin from a command dtk did not run — CI logs, or any invocation the hook
missed:

```sh
# Accurate verdict: the producing command's exit code is passed explicitly.
dotnet build > build.log 2>&1; dtk pipe build --exit-code $? < build.log

# Convenient form. Bash cannot give a pipeline's right-hand side its predecessor's
# status, so without --exit-code the verdict is assumed to be success.
dotnet build 2>&1 | dtk pipe build

dotnet list package --outdated 2>&1 | dtk pipe list package
```

`dtk pipe` exits with whatever `--exit-code` it was given, so a CI step wrapping a failed build
still fails. Piped runs are tracked separately from runs dtk executed itself under a `Source`
column in `dtk gain --coverage`.

### `dtk integrate`

Install dtk integration artifacts for an AI assistant provider:

```sh
dtk integrate claude      # Claude Code skill + PreToolUse hook
dtk integrate copilot     # GitHub Copilot instructions section
dtk integrate gemini      # Gemini CLI hook + settings merge
dtk integrate cursor      # Cursor rules file
dtk integrate windsurf    # Windsurf rules file
dtk integrate aider       # Aider instructions + .aider.conf.yml section
dtk integrate jetbrains   # JetBrains AI guidelines section
```

All commands accept `--force` to overwrite existing files and `--dir <path>` to target a specific directory.

See [AI Agent Setup](ai-agent-setup.md) for details on what each provider installs.

### `dtk gain`

Display token savings analytics. See [Token Analytics](token-analytics.md) for details.

```sh
dtk gain                       # last 30 days
dtk gain --days 7              # last 7 days
dtk gain --project             # current project only
dtk gain --command build       # filter to a specific command
dtk gain --json                # machine-readable JSON output
dtk gain --export csv          # export raw records as CSV
```

### `dtk log`

Retrieve a previous run's full output instead of re-running it:

```sh
dtk log                  # newest log for this project: last 100 lines
dtk log build            # newest build log for this project
dtk log --list           # what is available
dtk log --index 3        # the 3rd newest
dtk log --lines 300      # a wider window
dtk log --full           # everything
dtk log --all            # include other projects
```

Logs are written by the tee feature, which defaults to `tee.mode = Failures` — only failed runs are saved, and output under 500 characters is never saved. Use `dtk config set tee.mode Always` to keep every run. Logs written by dtk 0.6.0 or earlier carry no project metadata and appear only under `--all`.

Passthrough subcommands — anything dtk has no filter for, such as `publish` or `ef migrations` — are not tee'd at all, so `dtk log` will never find them.

### `dtk reset`

Clear tracking data (and optionally all dtk state):

```sh
dtk reset              # prompts for confirmation
dtk reset --force      # skips confirmation
dtk reset --all        # also removes tee logs and config file
dtk reset --all --force  # full cleanup without confirmation
```

### `dtk config`

View or modify configuration without editing the JSON file directly:

```sh
dtk config show                          # display all keys and current values
dtk config set <key> <value>             # update a single value and save
```

Examples:

```sh
dtk config set tracking.enabled false
dtk config set tracking.retentionDays 30
dtk config set display.emoji false
dtk config set tee.mode Always
dtk config set tracking.tokenizer O200kBase
```

Returns exit code `1` with an error message if the key is unknown or the value is invalid. See [Configuration](configuration.md) for the full list of supported keys and valid values.

### `dtk doctor`

Run self-diagnostic checks:

```sh
dtk doctor
```

Reports pass/fail for four checks: dotnet SDK availability, config file load, tracking database path access, and tee directory writability. Exits `0` if all pass, `1` if any fail.

### `dtk completion`

Print a shell completion script for the given shell:

```sh
dtk completion bash
dtk completion zsh
dtk completion fish
dtk completion powershell   # also accepts: pwsh
```

**bash** — write to a dedicated file (preferred) or to the system-wide completions directory:

```sh
dtk completion bash > ~/.bash_completion
# or system-wide:
dtk completion bash > /etc/bash_completion.d/dtk
```

**zsh** — the script must be autoloaded from `$fpath`; do not source it inline in `~/.zshrc`:

```sh
mkdir -p ~/.zfunc && dtk completion zsh > ~/.zfunc/_dtk
```

Then add these two lines to `~/.zshrc` before any existing `compinit` call:

```sh
fpath=(~/.zfunc $fpath)
autoload -Uz compinit && compinit
```

**fish** — write to the completions directory:

```sh
dtk completion fish > ~/.config/fish/completions/dtk.fish
```

**PowerShell** — append to your profile:

```pwsh
dtk completion powershell >> $PROFILE
```

## Quiet Mode

Add `-q` / `--quiet` to any `dtk dotnet` command to suppress DTK meta-output and forward only the filtered content. This is useful when piping output into other tools:

```sh
dtk dotnet build -q | grep "error"
dtk dotnet test -q > test-results.txt
```

In quiet mode, verbosity flags (`-v`, `--vv`) and `--show-log` are ignored — only the filtered command output is written.

## Passthrough Behavior

Any `dotnet` subcommand not in the supported list (build, test, restore, clean, format, list package) is passed through to `dotnet` unchanged:

```sh
dtk dotnet publish    # runs: dotnet publish
dtk dotnet pack       # runs: dotnet pack
dtk dotnet run        # runs: dotnet run
```

## Log Files

DTK can save the raw, unfiltered command output to disk. This is controlled by the `tee.mode` configuration setting (see [Configuration](configuration.md)). By default, only failed runs are saved.

Each log file also records the full command line and working directory that produced it, alongside the output. Log files are kept owner-only (mode `0600`) for that reason.

To print the log path after a command:

```sh
dtk dotnet test --show-log
```

Or retrieve a previous run's log without re-running anything: see [`dtk log`](#dtk-log) above.
