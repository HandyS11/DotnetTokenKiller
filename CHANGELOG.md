# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.10.0] - 2026-11-04

### Added

- `dtk test`, `dtk build`, `dtk restore`, `dtk clean`, `dtk format`, `dtk publish`, `dtk pack` and `dtk list package`
  work without the `dotnet` word and mean `dtk dotnet …`. `dtk test` used to answer "Unknown command 'test'. Did you
  mean 'reset'?", which points at the command that deletes your tracking data.
- A way to get `dotnet`'s own output when the filtered one is not enough. Set `DTK_RAW=1` (any non-empty value except
  `0` and `false`, in the environment or as a prefix) and `dtk dotnet …` runs `dotnet` unfiltered, recorded like any
  other passthrough, without dtk's own output options (`-q`, `--quiet`, `-v` without a level, `--verbose`, `--vv`,
  `--show-log`). The rewrite hook of every supported agent now also leaves alone a `dotnet` that the command asks for
  raw: `rtk proxy dotnet …`, `command dotnet …` and `DTK_RAW=1 dotnet …`, next to the `\dotnet` and absolute-path forms
  that already worked. What a hook auto-approves does not change.
- `dotnet test --list-tests` prints the listed test names, one per line, with the build noise removed, and ends with
  `✓ dotnet test --list-tests: 4 tests (1 project)`. It printed nothing before.
- A test run that compiled with warnings reports them at the end of its status line, naming at most three codes, most
  frequent first: `✓ dotnet test: 1 passed (1 project, 0.01s) · 3 build warnings (CS0219×2, CS8602)`. Only projects
  compiled in that run report any: an up-to-date build recompiles nothing, so it has none to report.
- `dtk doctor` checks that rtk and dtk do not both rewrite `dotnet`. When an rtk hook is registered but
  `rtk rewrite "dotnet build"` still rewrites, it warns and names the rtk config file to fix. It never edits that
  file.

### Changed

- **The summary line is now the last line of every filtered run.** A script that reads the first line of dtk's output
  must read the last one. `dotnet build`, `publish` and `pack` no longer open with a `dotnet build: 3 errors, 1
  warning` header and a `---` separator; the verdict closes the report instead, and on a failure it carries the exit
  code (`✗ dotnet build failed (exit 1): 3 errors, 1 warning (1 project, 1.89s)`; a clean build is still
  `✓ dotnet build (1 project, 1.19s)`). `dotnet restore` errors, `dotnet format` and every `dotnet list package`
  report close with their summary instead of opening with it, a failed `dotnet clean` now ends with one
  (`✗ dotnet clean: 1 error`), and the `--show-log` hint comes before the status line. A run whose filter has nothing
  to show prints the last 40 raw lines and then a status line: `⚠ <command>: output not recognized, raw tail shown
  above`, `✗ <command> failed (exit N)`, or `✓ <command> (no output)` when there was nothing at all, where `<command>`
  is the `dotnet` command line as dtk ran it (`dotnet test --no-build --filter X`; `dtk pipe` names only `dotnet test`).
  On a zero exit such a run used to print nothing, and on a failure it put the status line above the raw tail. A run
  that exits 0 but whose output counted errors ends with `⚠` rather than `✓`; the exit code stays the verdict.
- A failing `dotnet test` run's status line now opens with `✗` like every other failure line, where 0.9.0 printed it
  bare: `✗ dotnet test: 1 failed, 3 passed (1 project, 0.07s)`. A script that matches the start of that line must allow
  for the glyph.
- `dtk log` now keeps the newest run of each kind (build, test, format, …) in each project, passing or failing and
  whatever its size, so `dtk log` and `dtk log build` always show the latest run. In the default `tee.mode` of
  `Failures` a passing run's log used to be deleted as it finished, so after a passing run `dtk log` showed an older
  failed run as if it were the latest. The cost is disk: the tee directory now holds a log for each kind and project
  even when everything passes, each at most `tee.maxFileSizeBytes` (1 MB by default) and counted toward `tee.maxFiles`
  (20). The next run of that kind in that project replaces the previous passing log, and any older log under 500
  bytes; failing logs of 500 bytes or more stay until `tee.maxFiles` rotates them out. A passing run's raw output,
  test stdout included, is therefore now kept on disk by default (readable by its owner only on Linux and macOS; on
  Windows a log takes the permissions of its folder, by default `%LOCALAPPDATA%/dtk/tee`); set `tee.mode` to
  `Never` (`dtk config set tee.mode Never`) to turn the logs off.
- `-v <level>` now goes to `dotnet`: `dtk dotnet build -v q` used to take `-v` for dtk's `--verbose` and pass `q` on as
  a project path (MSB1009). Only a `-v` followed by `q`, `quiet`, `m`, `minimal`, `n`, `normal`, `d`, `detailed`,
  `diag` or `diagnostic` (any case) is left to `dotnet`; a bare `-v` is still dtk's verbose flag.
- `dotnet publish --interactive` and `dotnet pack --interactive` still pass through unfiltered with the terminal
  attached, but no longer hand dtk's own output options (`-q`, `--quiet`, `-v` without a level, `--verbose`, `--vv`,
  `--show-log`) to `dotnet`, which rejects them.
- Errors and warnings from `dotnet build`, `publish`, `pack` and `dotnet test` share one layout, grouped by file as
  `(line,col) CODE: message`, where errors were grouped by file and warnings by code. A file with a single line of
  diagnostics prints it on that line with no heading, and a diagnostic repeated in a file prints once with all its
  positions (`(96,59) (99,59) (111,59) CS1503 ×3: …`, at most 20, then `… +N more`). Messages are cut at 400
  characters for an error and 160 for a warning instead of 120, so CS1503 keeps its "to 'X'" and CS0121 its second
  overload, and help links and `System.` / `Microsoft.` prefixes on quoted type names are dropped. `Top codes` appears
  only when it adds something, and the project count is shown on a failed build too, counting a multi-targeted project
  once.
- Warnings hidden behind errors read `6 warnings not shown (dtk log)` instead of `6 warnings (3 shown) suppressed (use
  --vv to see)`: the failed run is already saved for `dtk log`, which costs far less than `--vv`'s raw dump.
- `dotnet format` problems are grouped by file and code, one line each, where each violation had a line of its own: the
  `[project.csproj]` suffix is gone, a whitespace change reads in words (`WHITESPACE ×7 (1:8 newline, 2:4 insert 1
  space, …)`), and the verdict is `✗ dotnet format: 18 violations in 2 files`. A line lists at most 20 positions, then
  `… +N more`. The report lists at most 40 group lines, then one `… 6 more groups in 2 files (dtk log)` line and the
  status line; the counts stay complete. The old cap of ten violations is gone.
- A `dotnet test` filter that matches nothing now says which one: `⚠ dotnet test: no test matched filter
  FullyQualifiedName~X (Probe.dll)`, where it said `0 tests found (no assembly matched)`. The exit code is still
  dotnet's own.
- `dtk gain` no longer records a loss: saved tokens are clamped at zero per run, so a filter that writes its own line
  for an empty input (`dotnet format` with nothing to do) shows `0` rather than `-20`. Runs already recorded are not
  rewritten.
- The instructions `dtk init <provider>` generates (the skill, the `AGENTS.md`, `GEMINI.md`, Copilot and JetBrains
  sections, and the Cursor, Devin and Aider rules) teach agents what this release added: `dtk test` without the
  `dotnet` word, `dtk log`, the raw-output forms, and not to pipe dtk's output into `tail` or `grep`, since the last
  line is the status. They no longer advise running `dtk dotnet clean` first for a full warning report, and they point
  to `dtk log` rather than `--vv` for a run's raw output. Re-run `dtk init <provider>` to refresh the skill, and add
  `--force` to refresh a section in an existing `AGENTS.md`; files written by earlier releases are still recognised by
  an upgrade and by `--uninstall`.

### Fixed

- A failed `dotnet clean` prints its error lines as MSBuild wrote them, with only the project root shortened. dtk used
  to treat each whole line as a path, so on Windows a line lost its final `.`.
- An MSBuild command-line error keeps the argument it rejected. After `MSB1009: Project file does not exist.` MSBuild
  names that argument on a line of its own (`Switch: nothere.csproj`), which `dotnet build`, `publish` and `pack` used
  to drop. It now ends the error's message: `(no file) MSB1009: Project file does not exist. Switch: nothere.csproj`.
- A `dotnet test` run whose test project does not compile (the red step of TDD) shows its compile errors grouped like
  `dotnet build` does, ending with `✗ dotnet test: 1 project did not build (2 errors)`, instead of the last 40 raw
  MSBuild lines, which lost the first errors and the total. When other projects ran, their failures follow the errors
  and one line names both counts: `✗ dotnet test: 1 failed, 2 passed · 1 project did not build (2 errors) (1 project,
  0.02s)`, where the trailing `(1 project, …)` counts the projects that ran tests. They were dropped silently before.
- `dotnet test` runs that printed nothing now print a result: a project whose tests are all skipped (VSTest's
  `Skipped!`), alone or next to others, a project that finished in under a millisecond (`Duration: < 1 ms`), and a run
  with the normal or detailed console logger (`Test Run Successful.` / `Test Run Failed.`), which also reported
  `1 failed, 0 passed` for a failing run that had passes.
- A failed test keeps its cause. A message may run to 300 characters (it was 200), and when it nests exceptions the
  innermost `---- Type : message` is kept (up to 200 characters) and the outer ones are shortened first. xUnit's
  assertion header (`Collections differ`, `Strings differ`, `Values differ`) and mismatch position survive
  (`Assert.Equal() Failure: Collections differ at pos 0 — Expected: [1, 2, 3], Actual: [9, 2, 3]`), and a pending
  Reqnroll step shows the step to implement (`No matching step definition: When the numbers are frobnicated`) instead of
  the generated binding snippet. xUnit v3 failures under Microsoft.Testing.Platform, a timeout included, keep their
  message instead of a blank line.
- `dtk dotnet list package --format json` and the MSBuild queries `-getProperty:`, `-getItem:` and `-getTargetResult:`
  (on `build`, `test`, `restore`, `clean`, `publish` and `pack`) pass through untouched, with the terminal attached
  and dtk's own output options (`-q`, `--quiet`, `-v` without a level, `--verbose`, `--vv`, `--show-log`) dropped. A
  filter used to append a line to the JSON document or replace the printed value with a status line, which broke
  `| jq .` and `-getProperty:Version`.
- Numbers and durations no longer follow the system culture: under a French locale a summary read `0,34s`. Durations
  also changed shape in every filter that prints one (build, publish, pack, test, restore and format). A minute or more
  now reads `1m03s`, and an hour `1h02m03s`, where 0.9.0 printed `63.21s` and `3723.40s`; a script that parses seconds
  out of a status line must read the new form. Under a minute, a duration the tool gives with fractions is unchanged
  (`12.50s`, `0.78s`). A source that only gives whole units no longer claims decimals: VSTest's `Duration: 12 s` prints
  `12s` rather than `12.00s`, `Duration: 1 m 40 s` prints `1m40s` rather than `100.00s`, and restore's `(in 2 sec)`
  prints `2s`.
- `dtk init` writes rtk's `[hooks] exclude_commands = ["dotnet"]` where rtk reads it: the path `rtk config` prints, or
  else the platform default (`$XDG_CONFIG_HOME` or `~/.config` on Linux, `~/Library/Application Support/rtk/config.toml`
  on macOS, `%APPDATA%\rtk\config.toml` on Windows, which is inferred and not verified). This applies to every
  provider that detects an rtk hook: Claude Code, Codex, Cursor, OpenCode, pi and oh-my-pi, Antigravity and Factory
  Droid. It wrote `$XDG_CONFIG_HOME` or `~/.config` on every system, so on macOS both hooks still rewrote
  `dotnet build`. Re-run `dtk init <provider>` to write the right file; an exclusion in the old place is left as it was.
  `dtk doctor`'s rtk check looks only at Claude Code's settings.
- `dtk config` with no subcommand shows the configuration instead of printing help and exiting 1.
- `dtk doctor` no longer lists a hook's checks twice when the working directory is your home directory.
- `dtk doctor`, `dtk gain` and dtk's other messages no longer wrap at 80 columns in the middle of a path when their
  output is piped or redirected.

## [0.9.0] - 2026-09-28

### Security

- Hardened the shell-aware `dotnet` command rewriter against command substitution, heredocs, and quoting edge
  cases that could let the Copilot CLI hook treat a disguised arbitrary shell command as "simple" and auto-approve
  it.
- The Copilot CLI hook auto-approves (`allow`) fewer commands. It still replies `allow` for a single, plain
  `dotnet build`, `test`, `restore`, `clean`, `format` or `list package` invocation, but now replies `ask` for a
  command with an environment-variable prefix (`FOO=1 dotnet build`), a `!` outside single quotes, a redirection, or
  a command substitution (even inside double quotes), and for every `dotnet publish` and `dotnet pack` it rewrites.

### Added

- Rewrite hooks for nine more coding agents, each installed by `dtk init <provider>` and probed by `dtk doctor`.
  In a project, each writes the shared `AGENTS.md` section and `.agents/skills/dotnet-token-killer` skill plus the
  file listed below; `--global` writes their equivalents under your home directory instead:
  - Codex CLI (`dtk init codex`): a `PreToolUse` hook in `.codex/hooks.json`. Codex runs it only once you approve
    it under `/hooks`.
  - OpenCode (`dtk init opencode`) and Kilo Code (`dtk init kilo`): a generated plugin, `.opencode/plugins/dtk.js`
    or `.kilo/plugin/dtk.js`.
  - Antigravity CLI (`dtk init antigravity`): a hook in `.agents/hooks.json` that replies `ask`, never `allow`.
  - pi and oh-my-pi (`dtk init pi`, `dtk init oh-my-pi`): a generated extension in `.pi/extensions/` or
    `.omp/extensions/`.
  - Factory Droid (`dtk init droid`): a `PreToolUse` hook for the `Execute` tool, in whichever of `hooks.json` and
    `settings.json` Droid reads its hooks from.
  - Crush 0.88.0 or later (`dtk init crush`): a marked section in `.crushrc`.
  - Amp (`dtk init amp`): a generated plugin in `.amp/plugins/dtk.js`.
- `dtk init cursor` also installs a rewrite hook in `.cursor/hooks.json`, and `dtk init cursor --global` installs
  that hook alone in `~/.cursor/hooks.json`. It auto-approves only the commands the Copilot CLI hook does.
- `dtk init devin` installs a rule and a rewrite hook for Devin Local and Devin CLI: `.devin/rules/dtk.md` and
  `.devin/hooks.v1.json` in a project. With `--global` it writes the hook under the `hooks` key of
  `~/.config/devin/config.json` (`%APPDATA%\devin\config.json` on Windows) and a section in
  `~/.codeium/windsurf/memories/global_rules.md`.
- `dtk init <provider> --uninstall` removes a previously installed provider integration and reports each file as
  removed, unchanged, or kept. It also recognizes, and removes, the unedited Cursor, Windsurf and Aider files an
  earlier dtk release wrote; a kept file's note says how to remove it anyway.
- Filters for `dotnet publish` and `dotnet pack`, summarizing diagnostics and output locations like `dotnet build`.
- Truncated tee logs are now marked with a `[dtk: output truncated at <N> bytes]` line, and `dtk log` warns when it
  prints a truncated log.
- A captured `dotnet` run (its stdio piped through dtk's filters) now stops cleanly on Ctrl+C. The child usually
  exits on its own within a 5-second grace period; dtk then shows the filtered output, saves the log, records the
  run, and exits with the child's own exit code. Only if the child is still running after 5 seconds (or the signal
  reached dtk alone) does dtk kill the process tree and exit `130`. An attached run (`dotnet run`, `dotnet watch`,
  or any passthrough run whose stdio stays attached to the terminal) is left to handle Ctrl+C itself; a second
  Ctrl+C ends dtk either way. SIGTERM (Linux/macOS only) stops the tree at once.
- The `dtk hook` rewrite now also covers `dotnet publish` and `dotnet pack`. A `dotnet publish --interactive` or
  `dotnet pack --interactive` run passes through unfiltered with the terminal attached, so a credential provider's
  prompt stays visible.

### Changed

- `dtk init windsurf` is now an alias of `dtk init devin`, since Windsurf is now Devin Desktop. It removes the
  `.windsurf/rules/dtk.md` an older dtk wrote, unless it was edited.
- `dtk doctor` can now report a warning, which leaves its exit code unchanged: a Codex hook not yet approved, a
  Codex project not yet trusted, or a Factory Droid hook in a file Droid does not read.
- Re-running `dtk init` on an instructions file whose dtk section is already current reports it as `unchanged`
  instead of suggesting `--force`.
- Local/dev builds now report version `0.0.0-dev` instead of a stale released version number.

### Fixed

- A settings file with comments or trailing commas now gets an error that names the cause, instead of "must
  contain a valid JSON object".
- `dotnet test` output is now parsed correctly when a project uses the Microsoft.Testing.Platform test runner.
- Build diagnostic deduplication no longer merges identical-looking diagnostics that come from different projects.
- `dotnet ef database drop`'s interactive confirmation prompt is passed through instead of being captured (and
  starved) by the passthrough filter.
- dtk no longer crashes at startup on Windows when it runs without a console (for example as a harness's child
  process or a service): it now keeps the default output encoding when it cannot switch to UTF-8.

## [0.8.0] - 2026-09-14

### Changed

- Stabilized the `0.8.0-beta.1` Native AOT release; no further user-facing changes.

## [0.8.0-beta.1] - 2026-09-14

### Added

- Native AOT packages for linux-x64, linux-arm64, linux-musl-x64, linux-musl-arm64, and osx-arm64, plus a Native AOT
  `dtk.exe` shim on win-x64, cutting cold-start time substantially.
- A native `dtk hook` replaces the Python-based hooks; `dtk integrate` is renamed to `dtk init`.

### Changed

- Tracking setup now starts in the background while the wrapped command runs, and output is counted as it streams,
  reducing per-command overhead.

### Fixed

- Hook registration merging now recognizes equivalent hook commands and keeps an already-identical registration
  instead of duplicating it.

## [0.7.2] - 2026-09-12

### Fixed

- Build diagnostics were silently under-reported in some cases; the reported count now matches the actual output.

## [0.7.1] - 2026-08-08

### Fixed

- `dtk doctor` now names the correct scope in its suggested hook remedies.

## [0.7.0] - 2026-08-08

### Added

- `dtk log` to retrieve a previous run's raw output.
- `dtk pipe` to filter output that dtk did not itself produce.
- `dotnet list package` output filtering.
- `dtk gain --coverage` to show tracking coverage, and a new rtk-style dashboard for `dtk gain`.
- Tee logs now stream as the command runs, so a killed run keeps whatever output was written before it was killed.
- Integration artifacts (hooks, skills) dtk previously generated now refresh themselves automatically when a
  newer dtk adds something they're missing, and `dtk doctor` now checks that an installed hook is registered,
  current, and actually fires.

### Fixed

- A per-assembly test no-match result no longer masks real test results from other assemblies.

## [0.6.0] - 2026-07-13

### Added

- A GitHub Copilot CLI integration provider (`dtk integrate copilot-cli`).
- `dtk integrate --global` to install AI agent integrations once for every project.
- dtk now takes over `dotnet` commands automatically when `rtk` is also installed, instead of conflicting with it.

### Fixed

- Filters now receive the wrapped command's exit code, so a failed run can no longer print a success summary.
- `dotnet build`/`test`/`restore` filters handle more real-world diagnostic formats correctly.
- Tee log hardening and deterministic child-process-tree termination when a run is interrupted.
- The Claude Code hook installer now writes a valid `updatedInput` schema and no longer duplicates hooks.
- Various CLI correctness fixes: `dtk gain` output, `--` argument forwarding, and verbosity handling.

## [0.5.0] - 2026-03-30

### Changed

- `dtk gain` now shows whether each tracked run succeeded.

## [0.4.0] - 2026-03-26

### Added

- `dotnet format` output filtering.

## [0.3.1] - 2026-03-25

### Fixed

- A missing command exclusion and other small correctness issues.

## [0.3.0] - 2026-03-24

### Changed

- Improved provider integration setup commands and their argument handling.

## [0.2.0] - 2026-03-22

### Added

- AI agent provider integration (`dtk integrate`) to set up hooks and skills for coding agents.
- A `dotnet-token-killer` Claude Code skill with usage documentation.

## [0.1.0] - 2026-03-19

### Added

- Initial release of `dtk`, a .NET CLI proxy that filters `dotnet build`, `test`, `restore`, `publish`, and `pack`
  output to reduce token usage.
- Token savings analytics (`dtk gain`).
- JSON configuration support.
- Packaging as a .NET tool (`dotnet tool install DotnetTokenKiller`).

[Unreleased]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.10.0...HEAD
[0.10.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.8.0-beta.1...v0.8.0
[0.8.0-beta.1]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.7.2...v0.8.0-beta.1
[0.7.2]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/HandyS11/DotnetTokenKiller/releases/tag/v0.1.0
