# Story 1.4: Implement Core Application Helpers and FilteredRunUseCase

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want the `FilteredRunUseCase` orchestrator and core helpers implemented,
so that any filter can be plugged in and the full run→filter→print→track pipeline works end to end.

## Acceptance Criteria

1. `FilteredRunUseCase.RunAsync(filter, command, args, verbosityLevel, ct)` calls `ICommandRunner.RunCapturedAsync`, combines stdout + stderr, strips ANSI codes, applies `filter.Apply(...)`, prints filtered output to console, and returns the process exit code unchanged
2. If `filter.Apply(...)` throws any exception, `FilteredRunUseCase` catches it, falls back to printing the ANSI-stripped raw output, and still returns the correct exit code (fail-safe — never break the user's workflow)
3. At verbosity level ≥ 1 (`verbosityLevel >= 1`), the command being executed is printed to console before running
4. At verbosity level ≥ 2 (`verbosityLevel >= 2`), raw (stripped) output is printed before filtered output, along with elapsed time in milliseconds
5. After filtering, `ITeeService.TeeAndHintAsync(raw, commandSlug, exitCode, ct)` is called; if it returns a non-null hint string, the hint is printed to console; any exception from tee is caught silently and never surfaces to the user
6. After tee, `ITracker.RecordAsync(record, ct)` is called with the correct `CommandRecord` (command, projectPath, inputTokens, outputTokens, savedTokens, savingsPercentage, executionTime); any exception from tracking is caught silently
7. `TokenEstimator.Estimate(string text)` returns `text.Length / 4` (integer division, truncated)
8. `AnsiStrip.Strip(string text)` removes all ANSI/VT100 CSI escape sequences using a `[GeneratedRegex]` pattern; returns the input unchanged if no sequences are found; never throws on any input
9. `TextHelpers.Truncate(string text, int maxLen)` returns text unchanged if `text.Length <= maxLen`; otherwise returns `text[..maxLen] + "..."`
10. `TextHelpers.FormatTokens(int count)` returns human-readable token counts: values <1,000 return digits only (e.g., `"850"`); values <1,000,000 return `"X.XK"` format (e.g., `"1.2K"`); values ≥1,000,000 return `"X.XM"` format (e.g., `"3.5M"`)
11. `TextHelpers.ShortenPath(string absolutePath, string rootPath)` converts an absolute path to a forward-slash relative path from rootPath; if the relative computation fails (e.g., different drive on Windows), falls back to `Path.GetFileName(absolutePath)`
12. All helpers are `public static` classes with `public static` methods, no instance state, no I/O
13. `FilteredRunUseCase` and all helpers have complete unit tests in `DotnetTokenKiller.Application.Tests`; `dotnet test DotnetTokenKiller.slnx` → all tests pass
14. `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings

## Tasks / Subtasks

- [ ] Task 1: Add NSubstitute to central package management (AC: #13)
  - [ ] Add `<PackageVersion Include="NSubstitute" Version="5.3.0"/>` to `Directory.Packages.props`
  - [ ] Add `<PackageReference Include="NSubstitute"/>` to `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj`

- [ ] Task 2: Add Microsoft.Extensions.DependencyInjection to Application project (AC: #1)
  - [ ] Add `<PackageReference Include="Microsoft.Extensions.DependencyInjection"/>` to `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj`
  - [ ] Create `src/DotnetTokenKiller.Application/DependencyInjection.cs` — `AddApplication(this IServiceCollection)` registering `FilteredRunUseCase` as transient

- [ ] Task 3: Create `AnsiStrip` helper (AC: #8, #12)
  - [ ] File: `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs`
  - [ ] `public static partial class AnsiStrip` with `public static string Strip(string text)`
  - [ ] Pattern covers all CSI sequences: `\x1b\[[0-9;]*[A-Za-z]` — use `[GeneratedRegex]` on a `private static partial` method

- [ ] Task 4: Create `TokenEstimator` helper (AC: #7, #12)
  - [ ] File: `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs`
  - [ ] `public static class TokenEstimator` with `public static int Estimate(string text)`
  - [ ] Returns `text.Length / 4`; handle null/empty safely (return 0)

- [ ] Task 5: Create `TextHelpers` helper (AC: #9, #10, #11, #12)
  - [ ] File: `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs`
  - [ ] `public static class TextHelpers` with `Truncate`, `FormatTokens`, `ShortenPath` static methods

- [ ] Task 6: Create `FilteredRunUseCase` (AC: #1, #2, #3, #4, #5, #6)
  - [ ] File: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs`
  - [ ] Primary constructor injecting: `ICommandRunner`, `ITracker`, `ITeeService`
  - [ ] `public async Task<int> RunAsync(IOutputFilter filter, string command, IReadOnlyList<string> args, int verbosityLevel, CancellationToken cancellationToken = default)`
  - [ ] See "Precise Implementation Signatures" section for full implementation

- [ ] Task 7: Wire `AddApplication()` into `Program.cs` (AC: #1)
  - [ ] Add `services.AddApplication()` call in `src/DotnetTokenKiller.Cli/Program.cs` after `services.AddInfrastructure()`
  - [ ] Add `using DotnetTokenKiller.Application;` (the DI extension namespace)

- [ ] Task 8: Write unit tests (AC: #7, #8, #9, #10, #11, #13)
  - [ ] `tests/DotnetTokenKiller.Application.Tests/Helpers/TokenEstimatorTests.cs`
  - [ ] `tests/DotnetTokenKiller.Application.Tests/Helpers/AnsiStripTests.cs`
  - [ ] `tests/DotnetTokenKiller.Application.Tests/Helpers/TextHelpersTests.cs`
  - [ ] `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`

- [ ] Task 9: Build and verify (AC: #13, #14)
  - [ ] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [ ] `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on 17 existing Domain tests)
  - [ ] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Critical: Current Repository State (After Stories 1.1 + 1.2 + 1.3)

All files below exist and MUST NOT be modified unless listed as a target:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, `LangVersion=14`, `TreatWarningsAsErrors=true` |
| `Directory.Packages.props` | Exists — **needs NSubstitute 5.3.0 added** |
| `src/DotnetTokenKiller.Domain/**` | Complete — all 9 Domain contracts + value objects |
| `src/DotnetTokenKiller.Infrastructure/**` | Complete — ProcessCommandRunner, NullTracker, NullConfigProvider, NullTeeService, DependencyInjection.cs |
| `src/DotnetTokenKiller.Cli/Program.cs` | Exists — **needs `services.AddApplication()` added** |
| `src/DotnetTokenKiller.Cli/Commands/*.cs` | All 10 dotnet stubs + GainCommand — **DO NOT MODIFY** |
| `tests/DotnetTokenKiller.Domain.Tests/**` | 17 passing tests — **DO NOT BREAK** |

**Application project is currently EMPTY** (no source files in `src/DotnetTokenKiller.Application/`).

**This story adds new files to**:

- `src/DotnetTokenKiller.Application/` (DependencyInjection.cs + UseCases/ + Helpers/)
- `tests/DotnetTokenKiller.Application.Tests/` (UseCases/ + Helpers/ subdirectories)

**Modifies**:

- `Directory.Packages.props` (add NSubstitute)
- `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj` (add MSDI package ref)
- `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj` (add NSubstitute)
- `src/DotnetTokenKiller.Cli/Program.cs` (add `services.AddApplication()`)

### Why the CLI Commands Are NOT Changed in This Story

`DotnetBuildCommand`, `DotnetTestCommand`, etc. currently call `commandRunner.RunPassthroughAsync` directly (Story 1.3 implementation). Wiring each command to `FilteredRunUseCase` happens in the filter stories:

- Story 1.5: `DotnetBuildCommand` → `FilteredRunUseCase` + `DotnetBuildFilter`
- Story 2.1: `DotnetTestCommand` → `FilteredRunUseCase` + `DotnetTestFilter`
- ...and so on per epic.

This story only creates the `FilteredRunUseCase` infrastructure so it can be injected in those stories.

### Architecture Dependency Rules (CRITICAL)

- `Application` references `Domain` only — adding MSDI is OK (it's a framework registration concern), but **never reference Infrastructure from Application**
- `FilteredRunUseCase` constructors accept only `Domain` interfaces (`ICommandRunner`, `ITracker`, `ITeeService`)
- Helpers (`AnsiStrip`, `TokenEstimator`, `TextHelpers`) have ZERO dependencies — pure functions only
- Registration of `FilteredRunUseCase` happens in `Program.cs` (CLI composition root) via `AddApplication()` extension in Application project, NOT in Infrastructure

### Precise Implementation Signatures

#### `DependencyInjection.cs` (Application project)

```csharp
namespace DotnetTokenKiller.Application;

using Microsoft.Extensions.DependencyInjection;
using DotnetTokenKiller.Application.UseCases;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        return services;
    }
}
```

**Note:** This is a separate class from `DotnetTokenKiller.Infrastructure.ServiceCollectionExtensions`. Both can coexist because the method names are the same but on different types — no collision.

#### `AnsiStrip.cs`

```csharp
namespace DotnetTokenKiller.Application.Helpers;

using System.Text.RegularExpressions;

public static partial class AnsiStrip
{
    // Matches all ANSI/VT100 CSI sequences: ESC [ ... final-byte
    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]")]
    private static partial Regex CsiPattern();

    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return CsiPattern().Replace(text, string.Empty);
    }
}
```

**Key points:**

- Class must be `partial` to host `[GeneratedRegex]` partial method
- Pattern covers SGR (colors), cursor movement, erase sequences — 99% of dotnet CLI output
- `string.IsNullOrEmpty` guard prevents null-reference analyzer warnings

#### `TokenEstimator.cs`

```csharp
namespace DotnetTokenKiller.Application.Helpers;

public static class TokenEstimator
{
    public static int Estimate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return text.Length / 4;
    }
}
```

#### `TextHelpers.cs`

```csharp
namespace DotnetTokenKiller.Application.Helpers;

public static class TextHelpers
{
    public static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
            return text ?? string.Empty;

        return string.Concat(text.AsSpan(0, maxLen), "...");
    }

    public static string FormatTokens(int count)
    {
        if (count >= 1_000_000)
            return $"{count / 1_000_000.0:F1}M";

        if (count >= 1_000)
            return $"{count / 1_000.0:F1}K";

        return count.ToString();
    }

    public static string ShortenPath(string absolutePath, string rootPath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return absolutePath ?? string.Empty;

        try
        {
            var relative = Path.GetRelativePath(rootPath, absolutePath);
            return relative.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(absolutePath);
        }
    }
}
```

**Notes:**

- `Truncate`: uses `string.Concat(AsSpan, ...)` to avoid an intermediate substring allocation
- `FormatTokens`: `1200` → `"1.2K"`, `3_500_000` → `"3.5M"`; boundary is inclusive (exactly 1000 → `"1.0K"`)
- `ShortenPath`: `Path.GetRelativePath` is cross-platform and handles null rootPath by throwing — caught and fallback to filename; always convert `\` to `/`

#### `FilteredRunUseCase.cs`

```csharp
namespace DotnetTokenKiller.Application.UseCases;

using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;

public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker,
    ITeeService teeService)
{
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string command,
        IReadOnlyList<string> args,
        int verbosityLevel,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (verbosityLevel >= 1)
            Console.WriteLine($"$ {command} {string.Join(' ', args)}");

        var result = await commandRunner.RunCapturedAsync(command, args, cancellationToken);

        var raw = result.StdOut + result.StdErr;
        var stripped = AnsiStrip.Strip(raw);

        string filtered;
        try
        {
            filtered = filter.Apply(stripped);
        }
        catch
        {
            if (verbosityLevel >= 2)
                Console.WriteLine("[filter error — using raw output]");
            filtered = stripped;
        }

        if (verbosityLevel >= 2)
        {
            Console.WriteLine("[raw output]");
            Console.WriteLine(stripped);
            Console.WriteLine($"[elapsed: {stopwatch.ElapsedMilliseconds}ms]");
        }

        Console.Write(filtered);

        stopwatch.Stop();

        // Tee: silent — errors never surface
        try
        {
            var commandSlug = args.Count > 0 ? args[0] : command;
            var hint = await teeService.TeeAndHintAsync(stripped, commandSlug, result.ExitCode, cancellationToken);
            if (hint is not null)
                Console.WriteLine(hint);
        }
        catch { }

        // Track: silent — errors never surface
        try
        {
            var inputTokens = TokenEstimator.Estimate(stripped);
            var outputTokens = TokenEstimator.Estimate(filtered);
            var savedTokens = inputTokens - outputTokens;
            var savingsPct = inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0;

            var record = new CommandRecord(
                Timestamp: DateTimeOffset.UtcNow,
                Command: args.Count > 0 ? args[0] : command,
                ProjectPath: Environment.CurrentDirectory,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                SavedTokens: savedTokens,
                SavingsPercentage: savingsPct,
                ExecutionTime: stopwatch.Elapsed);

            await tracker.RecordAsync(record, cancellationToken);
        }
        catch { }

        return result.ExitCode;
    }
}
```

**Critical notes:**

- `raw = result.StdOut + result.StdErr` — combine without separator; each stream's lines already end with `\n`
- ANSI stripping happens BEFORE filter AND before token estimation — always work on clean text
- `filter.Apply` exception → fall back to `stripped` (NOT `raw` — already have clean text)
- Verbosity output before `Console.Write(filtered)` — raw and timing shown before the filtered output
- `stopwatch.Stop()` after `Console.Write(filtered)` so elapsed time includes console write overhead
- `commandSlug` uses `args[0]` (e.g., `"build"` from `["build", "--configuration", "Release"]`)
- `CommandRecord.Command` stores the subcommand only (e.g., `"build"`) not full `"dotnet build"`
- Empty `catch { }` blocks are intentional and required — tracking/tee MUST NOT surface errors

#### Updated `Program.cs` (add one line)

Find the line `services.AddInfrastructure();` and add immediately after:

```csharp
services.AddApplication();
```

Add the `using` at the top:

```csharp
using DotnetTokenKiller.Application;
```

### Unit Test Implementation Patterns

#### `TokenEstimatorTests.cs`

```csharp
namespace DotnetTokenKiller.Application.Tests.Helpers;

using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

public class TokenEstimatorTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("1234", 1)]
    [InlineData("12345678", 2)]
    [InlineData("123456789012", 3)]  // 12 / 4 = 3
    public void Estimate_ReturnsLengthDividedByFour(string text, int expected)
    {
        TokenEstimator.Estimate(text).Should().Be(expected);
    }

    [Fact]
    public void Estimate_NullInput_ReturnsZero()
    {
        TokenEstimator.Estimate(null!).Should().Be(0);
    }

    [Fact]
    public void Estimate_LargeText_ReturnsCorrectCount()
    {
        var text = new string('x', 4000);
        TokenEstimator.Estimate(text).Should().Be(1000);
    }
}
```

#### `AnsiStripTests.cs`

```csharp
namespace DotnetTokenKiller.Application.Tests.Helpers;

using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

public class AnsiStripTests
{
    [Fact]
    public void Strip_PlainText_ReturnsUnchanged()
    {
        AnsiStrip.Strip("hello world").Should().Be("hello world");
    }

    [Fact]
    public void Strip_AnsiColorCode_RemovesCode()
    {
        AnsiStrip.Strip("\x1b[32mGREEN\x1b[0m").Should().Be("GREEN");
    }

    [Fact]
    public void Strip_MultipleSequences_RemovesAll()
    {
        AnsiStrip.Strip("\x1b[1m\x1b[31mERROR\x1b[0m: bad thing").Should().Be("ERROR: bad thing");
    }

    [Fact]
    public void Strip_EmptyString_ReturnsEmpty()
    {
        AnsiStrip.Strip(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void Strip_NullInput_ReturnsNull()
    {
        AnsiStrip.Strip(null!).Should().BeNull();
    }
}
```

#### `TextHelpersTests.cs`

```csharp
namespace DotnetTokenKiller.Application.Tests.Helpers;

using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

public class TextHelpersTests
{
    // Truncate
    [Theory]
    [InlineData("hello", 10, "hello")]
    [InlineData("hello world", 5, "hello...")]
    [InlineData("hello", 5, "hello")]
    public void Truncate_ReturnsExpected(string text, int maxLen, string expected)
    {
        TextHelpers.Truncate(text, maxLen).Should().Be(expected);
    }

    // FormatTokens
    [Theory]
    [InlineData(0, "0")]
    [InlineData(850, "850")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(1200, "1.2K")]
    [InlineData(999_999, "1000.0K")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(3_500_000, "3.5M")]
    public void FormatTokens_ReturnsExpected(int count, string expected)
    {
        TextHelpers.FormatTokens(count).Should().Be(expected);
    }

    // ShortenPath
    [Fact]
    public void ShortenPath_AbsolutePath_ReturnsRelativeWithForwardSlashes()
    {
        var root = "/home/user/project";
        var abs = "/home/user/project/src/Foo/Bar.cs";
        TextHelpers.ShortenPath(abs, root).Should().Be("src/Foo/Bar.cs");
    }

    [Fact]
    public void ShortenPath_EmptyInput_ReturnsEmpty()
    {
        TextHelpers.ShortenPath(string.Empty, "/root").Should().Be(string.Empty);
    }
}
```

**Note on `FormatTokens` boundary `999_999`:** `999_999 / 1_000.0 = 999.999` → formatted as `"1000.0K"`. This is technically correct but looks odd. If this is undesirable, the test expectation can be adjusted — verify the output and commit the actual behavior as the baseline. The important thing is the method doesn't throw.

#### `FilteredRunUseCaseTests.cs`

```csharp
namespace DotnetTokenKiller.Application.Tests.UseCases;

using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class FilteredRunUseCaseTests
{
    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();
    private readonly FilteredRunUseCase _sut;

    public FilteredRunUseCaseTests()
    {
        _sut = new FilteredRunUseCase(_runner, _tracker, _teeService);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCodeFromCommand()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 42));
        _filter.Apply(Arg.Any<string>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var exitCode = await _sut.RunAsync(_filter, "dotnet", new[] { "build" }, verbosityLevel: 0);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_FilterThrows_FallsBackToRawOutput_StillReturnsExitCode()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", new[] { "build" }, verbosityLevel: 0);

        await act.Should().NotThrowAsync();
        var exitCode = await _sut.RunAsync(_filter, "dotnet", new[] { "build" }, verbosityLevel: 0);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_TrackingThrows_DoesNotSurfaceException()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>()).Returns("filtered");
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("db error"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", new[] { "build" }, verbosityLevel: 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_TeeThrows_DoesNotSurfaceException()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("io error"));

        var act = async () => await _sut.RunAsync(_filter, "dotnet", new[] { "build" }, verbosityLevel: 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_CallsFilterWithCombinedStrippedOutput()
    {
        var stdout = "\x1b[32mHello\x1b[0m\n";
        var stderr = "\x1b[31mError\x1b[0m\n";
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(stdout, stderr, 0));
        _filter.Apply(Arg.Any<string>()).Returns("ok");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", new[] { "build" }, verbosityLevel: 0);

        // Filter should receive ANSI-stripped combined output
        _filter.Received(1).Apply("Hello\nError\n");
    }
}
```

**Testing note on console output:** `FilteredRunUseCase` writes to `Console.Out`. The tests above only verify behavior (exit codes, exception propagation, method calls) — NOT console output format. Capturing console output in Application.Tests is not required for this story.

### Package Management (CRITICAL)

```xml
<!-- Directory.Packages.props — add inside existing <ItemGroup> -->
<PackageVersion Include="NSubstitute" Version="5.3.0"/>
```

```xml
<!-- tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj -->
<!-- Add to existing <ItemGroup> with PackageReferences -->
<PackageReference Include="NSubstitute"/>
```

```xml
<!-- src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj -->
<!-- Add new <ItemGroup> -->
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
</ItemGroup>
```

**Never specify versions in `.csproj` files** — only in `Directory.Packages.props`.

### File Structure to Create

```sh
src/DotnetTokenKiller.Application/
  DependencyInjection.cs                      # AddApplication() extension
  Helpers/
    AnsiStrip.cs
    TokenEstimator.cs
    TextHelpers.cs
  UseCases/
    FilteredRunUseCase.cs

tests/DotnetTokenKiller.Application.Tests/
  Helpers/
    AnsiStripTests.cs
    TokenEstimatorTests.cs
    TextHelpersTests.cs
  UseCases/
    FilteredRunUseCaseTests.cs
```

### Analyzer Pitfalls (CRITICAL)

- **CA1849** — Do NOT use `.Result` or `.GetAwaiter().GetResult()` on Tasks; always `await`
- **CA1068** — `CancellationToken` MUST be the last parameter (all signatures above comply)
- **CA1852** — `FilteredRunUseCase` must be `sealed` (not inherited; mark it sealed)
- **S6966** — Same as CA1849: always await async methods
- **IDE0290** — Primary constructors preferred; `FilteredRunUseCase(ICommandRunner, ITracker, ITeeService)` uses primary constructor pattern
- **RCS1032** — `catch { }` empty blocks: SonarAnalyzer may flag these as S108. Add a comment to silence: `catch { /* intentional: errors must not surface */ }` OR suppress in `.editorconfig` as `dotnet_diagnostic.S108.severity = none`
- **`[GeneratedRegex]`** — `AnsiStrip` class MUST be `partial` for the generated method; missing `partial` is a build error
- **`using System.Diagnostics;`** — NOT in implicit usings; must be explicit in `FilteredRunUseCase.cs`
- **`using System.Text.RegularExpressions;`** — NOT implicit; must be explicit in `AnsiStrip.cs`
- **Namespace must match folder path**: `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` → `namespace DotnetTokenKiller.Application.Helpers;`

### Empty Catch Block Resolution (S108/S6966)

SonarAnalyzer S108 fires on `catch { }`. Choose ONE approach:

Option A — Comment in the catch (preferred, no editorconfig change):

```csharp
catch
{
    // Intentional: tracking/tee errors must never surface to the user
}
```

Option B — Suppress in `.editorconfig`:

```sh
[*.cs]
dotnet_diagnostic.S108.severity = none
```

**Use Option A** (comments are self-documenting; editorconfig suppression affects all code).

### What This Story Does NOT Implement (Scope Guard)

- `DotnetBuildFilter`, `DotnetTestFilter`, etc. — Stories 1.5, 2.1, 3.x, 4.x
- `SqliteTracker` — Story 5.1 (still using `NullTracker`)
- `JsonConfigProvider` — Story 6.1 (still using `NullConfigProvider`)
- `FileTeeService` — Story 6.2 (still using `NullTeeService`)
- `PassthroughRunUseCase` — Story 4.6 (fallback for unrecognized subcommands)
- `GainReportUseCase` — Story 5.3
- Changing any CLI command to use `FilteredRunUseCase` — Stories 1.5+
- GitHub Actions CI — Story 1.6
- `Verify.Xunit` snapshot testing — Story 1.5 (first filter story)
- `IConfigProvider` dependency in `FilteredRunUseCase` — not needed; tee/tracker handle their own config internally

### Project Structure Notes

- File-scoped namespaces everywhere (`namespace Foo;` not `namespace Foo { }`)
- Namespace must match folder exactly: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` → `namespace DotnetTokenKiller.Application.UseCases;`
- LF line endings, no trailing whitespace, 4-space indent for `.cs`, 2-space for `.csproj`
- `var` for all local variable declarations
- `_camelCase` for private fields (primary constructor params are NOT fields — no underscore needed)

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.4]
- [Source: _bmad-output/planning-artifacts/Architecture.md#4. Layer Responsibilities]
- [Source: _bmad-output/planning-artifacts/Architecture.md#6. Core Data Flow — FilteredRunUseCase]
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design]
- [Source: _bmad-output/planning-artifacts/Architecture.md#8. Infrastructure Details — Token Estimation]
- [Source: _bmad-output/planning-artifacts/Architecture.md#9. DI Registration]
- [Source: _bmad-output/project-context.md#Clean Architecture — Dependency Rules]
- [Source: _bmad-output/project-context.md#FilteredRunUseCase — Orchestration]
- [Source: _bmad-output/project-context.md#Critical Don't-Miss Rules]
- [Source: _bmad-output/project-context.md#Language-Specific Rules — GeneratedRegex]
- [Source: _bmad-output/implementation-artifacts/1-3-implement-process-execution-and-cli-entry-point.md#Dev Agent Record]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
