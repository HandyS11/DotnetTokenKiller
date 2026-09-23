# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Security

- Hardened the shell-aware `dotnet` command rewriter against command substitution, heredocs, and quoting edge
  cases that could let the Copilot CLI hook treat a disguised arbitrary shell command as "simple" and auto-approve
  it.

### Added

- `dtk init <provider> --uninstall` removes a previously installed provider integration and reports each file as
  removed, unchanged, or kept.
- Filters for `dotnet publish` and `dotnet pack`, summarizing diagnostics and output locations like `dotnet build`.
- Truncated tee logs are now marked with a `[dtk: output truncated at <N> bytes]` line, and `dtk log` warns when it
  prints a truncated log.
- A captured `dotnet` run (its stdio piped through dtk's filters) now stops cleanly on Ctrl+C: dtk gives the
  child 5 seconds to exit on its own, still draining, logging, and recording its output, before killing the
  process tree and exiting `130`. An attached run (`dotnet run`, `dotnet watch`, or any passthrough run whose
  stdio stays attached to the terminal) is left to handle Ctrl+C itself; a second Ctrl+C ends dtk either way.
  SIGTERM (Linux/macOS only) stops the tree at once.

### Changed

- Local/dev builds now report version `0.0.0-dev` instead of a stale released version number.

### Fixed

- `dotnet test` output is now parsed correctly when a project uses the Microsoft.Testing.Platform test runner.
- Build diagnostic deduplication no longer merges identical-looking diagnostics that come from different projects.
- `dotnet ef database drop`'s interactive confirmation prompt is passed through instead of being captured (and
  starved) by the passthrough filter.

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

[Unreleased]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.8.0...HEAD
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
