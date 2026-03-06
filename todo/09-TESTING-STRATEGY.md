# 09 — Testing Strategy

## Overview

Testing follows the Clean Architecture layer boundaries. Each layer has its own test project with appropriate concerns:

| Test Project | Targets | What It Tests |
|---|---|---|
| `DotnetTokenKiller.Domain.Tests` | Domain layer | Entity validation, value object equality |
| `DotnetTokenKiller.Application.Tests` | Application layer | Filters, use cases, helpers |
| `DotnetTokenKiller.Infrastructure.Tests` | Infrastructure layer | SQLite tracker, config loading, tee service |
| `DotnetTokenKiller.Cli.IntegrationTests` | Full CLI | End-to-end command execution |

### Key Principles

1. **Snapshot tests** — verify output format doesn't change accidentally
2. **Token accuracy tests** — verify ≥60% savings on real fixtures
3. **Real fixtures** — use actual `dotnet` command output, not synthetic data
4. **Edge cases** — empty input, malformed output, Unicode, ANSI codes
5. **Clean Architecture isolation** — Application tests mock Domain interfaces; no real I/O

---

## Test Framework Stack

| Package | Purpose |
|---|---|
| `xunit` | Test framework (`[Fact]`, `[Theory]`) |
| `FluentAssertions` | Expressive assertions |
| `Verify.Xunit` | Snapshot testing |
| `NSubstitute` | Mocking Domain interfaces (for use case tests) |
| `Microsoft.NET.Test.Sdk` | Test runner infrastructure |

All test projects target `net10.0`.

---

## Application Layer Tests (Filters)

This is where the bulk of testing effort goes. Filters are pure functions — easy to test with fixtures.

### Creating Fixtures

Capture real `dotnet` command output:

```powershell
# Create fixtures directory
mkdir tests/DotnetTokenKiller.Application.Tests/Fixtures

# Capture real output
dotnet build 2>&1 > Fixtures/dotnet_build_success.txt
dotnet test 2>&1 > Fixtures/dotnet_test_all_pass.txt
dotnet restore 2>&1 > Fixtures/dotnet_restore_raw.txt
dotnet publish -c Release 2>&1 > Fixtures/dotnet_publish_raw.txt
dotnet clean 2>&1 > Fixtures/dotnet_clean_raw.txt
dotnet format --verify-no-changes 2>&1 > Fixtures/dotnet_format_check.txt
```

To capture failure output, temporarily break something and run:

```powershell
dotnet build 2>&1 > Fixtures/dotnet_build_errors.txt
dotnet test 2>&1 > Fixtures/dotnet_test_failures.txt
```

Fixtures are included as **embedded resources** in the test project for portable access.

### Fixture Loading

A helper class loads embedded resource fixtures by name and provides token counting for savings verification tests.

### Snapshot Testing with Verify

Snapshot tests ensure filter output format doesn't change unintentionally:

1. Filter processes a fixture → produces output string
2. `Verify()` compares output against a `.verified.txt` file
3. On first run, creates `.received.txt` for review
4. After approval, `.verified.txt` becomes the baseline

Snapshot workflow:
```powershell
dotnet test                    # Creates .received.txt files for new snapshots
dotnet tool install -g verify.tool
dotnet verify accept           # Approve snapshots → .verified.txt
```

Snapshot files live alongside test files:
```
DotnetTokenKiller.Application.Tests/
├── Filters/
│   ├── DotnetBuildFilterTests.cs
│   ├── DotnetBuildFilterTests.SuccessBuild_ProducesCompactOutput.verified.txt
│   └── DotnetBuildFilterTests.ErrorBuild_ShowsGroupedErrors.verified.txt
```

---

## Token Savings Tests

Every filter **must** verify ≥60% token savings against real fixture data. This is a hard quality gate.

### Savings Targets

| Filter | Expected Savings | Rationale |
|---|---|---|
| `dotnet build` (success) | 85%+ | Replace ~20 lines with 1 line |
| `dotnet build` (errors) | 70%+ | Strip duplicates, keep grouped errors |
| `dotnet test` (all pass) | 90%+ | Replace ~30 lines with 1 line |
| `dotnet test` (failures) | 70%+ | Strip passing tests, keep failure details |
| `dotnet restore` | 90%+ | One-line summary |
| `dotnet clean` | 95%+ | Two words |
| `dotnet publish` | 80%+ | One-line with path |

The test calculates: `savings = 100 - (outputTokens / inputTokens * 100)` and asserts `savings >= 60`.

---

## Unit Tests (Behavioral)

Beyond snapshots, test specific behaviors:

- **Success builds** produce output starting with `✓ dotnet build`
- **Error builds** contain the error count and diagnostic codes
- **Test failures** list each failed test with source location
- **Path shortening** converts absolute paths to relative
- **Noise lines** (MSBuild version, restore progress) are stripped
- **Diagnostics are deduplicated** (MSBuild prints errors twice)

---

## Edge Case Tests

Every filter must handle these scenarios without crashing:

| Scenario | Expected Behavior |
|---|---|
| Empty string input | Return non-null result |
| Malformed / non-dotnet output | Return raw input or best-effort parse |
| Unicode content | Handle gracefully, no crashes |
| ANSI escape codes | Strip before processing |
| Very large output (>1MB) | Process without timeout or OOM |
| Null input | Handle defensively |

Use `[Theory]` with `[InlineData]` to test all filter types against these edge cases in a single parameterized test.

---

## Application Layer Use Case Tests

Test `FilteredRunUseCase` with mocked dependencies:

- Mock `ICommandRunner` to return predefined `CommandResult`
- Mock `ITeeService` to verify tee is called on failure
- Mock `ITracker` to verify token counts are recorded
- Verify that filter exceptions trigger fall-back to raw output
- Verify exit code propagation

This tests the orchestration logic without any real process execution or I/O.

---

## Infrastructure Tests

### `SqliteTracker` Tests

- Use in-memory SQLite (`Data Source=:memory:`) for fast, isolated tests
- Verify schema creation, record insertion, cleanup, summary queries
- Test database path resolution (env var, default)

### `JsonConfigProvider` Tests

- Test loading default config when no file exists
- Test round-trip: save → load → verify equality
- Use a temp directory so tests don't affect real config

### `FileTeeService` Tests

- Verify tee file is created on failure
- Verify tee is skipped on success (in "failures" mode)
- Verify old file cleanup respects max file count
- Verify file size truncation

---

## CLI Integration Tests

Run the actual `dtk` binary against a real .NET project:

- Mark with `[Trait("Category", "Integration")]` for selective execution
- Verify output length is significantly less than raw `dotnet` output
- Verify exit code preservation (0 for success, non-zero for failure)
- These tests require the `dtk` tool to be installed and a .NET project available

Skip by default in CI; run as a separate pipeline step.

---

## Pre-Commit Quality Gate

```powershell
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
```

This ensures code style, zero warnings, and all tests pass before every commit.

---

## Test Checklist (Per Filter)

When adding a new filter:

- [ ] Create fixture from real `dotnet` command output
- [ ] Add snapshot test with `Verify(output)`
- [ ] Add token savings test (verify ≥60%)
- [ ] Test empty input
- [ ] Test malformed input
- [ ] Test Unicode content
- [ ] Test ANSI escape codes
- [ ] Test multiple projects (solution-level output)
- [ ] Run `dotnet test` — all pass
- [ ] Run `dotnet verify accept` — snapshots reviewed
