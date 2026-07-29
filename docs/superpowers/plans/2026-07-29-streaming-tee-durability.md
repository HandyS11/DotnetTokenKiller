# Streaming Tee Durability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Invert the tee log from write-after-exit to write-during-run, so a dtk process killed by Ctrl-C or an agent tool-call timeout still leaves a readable log on disk.

**Architecture:** A new `ITeeSession` owns an open `FileStream` for the duration of a run. `FileTeeService.BeginAsync` rotates, creates the file 0600, and writes a v2 header with `status: running` *before* the child process starts; output lines append as they arrive through `ICommandRunner.RunStreamedAsync`, which already exists and already streams. `FinalizeAsync` seeks back and overwrites the fixed-width `status`/`exit` fields in place, or deletes the file when the existing retention rules say it should not be kept. Nothing depends on a cancellation handler, because dtk's cancellation token never fires in production.

**Tech Stack:** net10.0, xunit, FluentAssertions, NSubstitute, Spectre.Console.Cli 0.55.

**Spec:** [2026-07-29-streaming-tee-durability-design.md](../specs/2026-07-29-streaming-tee-durability-design.md)

## Global Constraints

- `TreatWarningsAsErrors` is enabled — Roslynator, SonarAnalyzer and NetAnalyzers warnings all fail the build.
- File-scoped namespaces; `var` throughout; private fields `_camelCase`; async methods end in `Async`; interfaces `IPascalCase`.
- LF line endings only, no trailing whitespace, no BOM, 4-space indent for `.cs`.
- Every public member needs an XML doc comment (the build enforces it).
- All version numbers live in `Directory.Packages.props`; `.csproj` files omit them. This plan adds no packages.
- Build and test with `dtk`, not raw `dotnet`: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`.
- `DotnetTokenKiller.Infrastructure` has **no** `InternalsVisibleTo`, so anything its tests touch must be `public`. `DotnetTokenKiller.Application` and `.Domain` do have it, so `internal` is fine there.
- Tee failures must never surface to the user. Every catch that enforces this must carry a comment saying so, matching the existing style.
- Fixed field widths, used in several tasks: status = 8 chars, exit = 11 chars (`-2147483648` is the widest `int`).

---

### Task 1: Header v2 with a running status

`TeeLogHeader` currently parses `# exit:` with `int.TryParse` and returns `false` on anything else, so it cannot express "this run has not finished". This task makes `ExitCode` nullable, derives `Status` from it (so the two can never disagree in memory), and moves `status`/`exit` to the end of the header as adjacent fixed-width fields so finalizing is a single contiguous overwrite.

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Tee/TeeLogStatus.cs`
- Modify: `src/DotnetTokenKiller.Domain/Tee/TeeLogHeader.cs`
- Test: `tests/DotnetTokenKiller.Domain.Tests/Tee/TeeLogHeaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TeeLogStatus { Running, Complete }`; `TeeLogHeader(string CommandLine, string ProjectPath, int? ExitCode, RunSource Source, DateTimeOffset TimestampUtc)` with `TeeLogStatus Status { get; }`, `string Render()`, `static string RenderStatusAndExit(int? exitCode)`, `static bool TryParse(string, out TeeLogHeader)`, `static string StripHeader(string)`, `const string VersionLine = "# dtk-log v2"`, `const string LegacyVersionLine = "# dtk-log v1"`, `const string Delimiter = "---"`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/DotnetTokenKiller.Domain.Tests/Tee/TeeLogHeaderTests.cs`:

```csharp
[Fact]
public void RenderStatusAndExit_ProducesTheSameLength_ForRunningAndAnyExitCode()
{
    // The finalize path overwrites this region in place at a fixed byte offset. If the
    // running and complete forms differ in length, that overwrite corrupts the delimiter
    // and every log from a finished run becomes unparseable.
    var running = TeeLogHeader.RenderStatusAndExit(null);
    var zero = TeeLogHeader.RenderStatusAndExit(0);
    var widest = TeeLogHeader.RenderStatusAndExit(int.MinValue);

    zero.Length.Should().Be(running.Length);
    widest.Length.Should().Be(running.Length);
}

[Fact]
public void Render_ThenTryParse_RoundTripsACompletedRun()
{
    var original = new TeeLogHeader(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        1,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

    TeeLogHeader.TryParse(original.Render(), out var parsed).Should().BeTrue();

    parsed.Should().Be(original);
    parsed.Status.Should().Be(TeeLogStatus.Complete);
}

[Fact]
public void Render_ThenTryParse_RoundTripsARunningRun()
{
    var original = new TeeLogHeader(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        null,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

    TeeLogHeader.TryParse(original.Render(), out var parsed).Should().BeTrue();

    parsed.ExitCode.Should().BeNull();
    parsed.Status.Should().Be(TeeLogStatus.Running);
}

[Fact]
public void TryParse_ReadsAV1Header_AsComplete()
{
    // Logs written before this change must keep listing; they are complete by definition,
    // because v1 could only be written after the process exited.
    const string v1 =
        "# dtk-log v1\n"
        + "# command: dotnet build MyApp.slnx\n"
        + "# cwd: /home/user/projects/MyApp\n"
        + "# exit: 1\n"
        + "# source: Run\n"
        + "# utc: 2026-07-28T09:14:02.0000000+00:00\n"
        + "---\n"
        + "body\n";

    TeeLogHeader.TryParse(v1, out var parsed).Should().BeTrue();

    parsed.ExitCode.Should().Be(1);
    parsed.Status.Should().Be(TeeLogStatus.Complete);
    parsed.CommandLine.Should().Be("dotnet build MyApp.slnx");
}

[Fact]
public void TryParse_RejectsAHeaderWhoseStatusAndExitDisagree()
{
    // status and exit are two spellings of one fact. A file where they disagree is corrupt,
    // not merely unfinished, and must not be read as either.
    const string contradictory =
        "# dtk-log v2\n"
        + "# command: dotnet build MyApp.slnx\n"
        + "# cwd: /home/user/projects/MyApp\n"
        + "# source: Run\n"
        + "# utc: 2026-07-28T09:14:02.0000000+00:00\n"
        + "# status: running \n"
        + "# exit:   0          \n"
        + "---\n";

    TeeLogHeader.TryParse(contradictory, out _).Should().BeFalse();
}

[Fact]
public void StripHeader_RemovesAV2Header()
{
    var header = new TeeLogHeader(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        0,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

    TeeLogHeader.StripHeader(header.Render() + "line one\nline two\n")
        .Should().Be("line one\nline two\n");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~TeeLogHeaderTests"`
Expected: compile errors — `TeeLogStatus` does not exist, `RenderStatusAndExit` does not exist, `int?` cannot be passed where `int` is expected.

- [ ] **Step 3: Create `TeeLogStatus`**

Create `src/DotnetTokenKiller.Domain/Tee/TeeLogStatus.cs`:

```csharp
namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Whether a tee log's run finished.</summary>
public enum TeeLogStatus
{
    /// <summary>
    /// The run had not finished when the log was last written. Either it is in flight, or dtk was
    /// killed before it could finalize — the two are indistinguishable from the file alone.
    /// </summary>
    Running,

    /// <summary>The run finished and its exit code was recorded.</summary>
    Complete
}
```

- [ ] **Step 4: Rewrite `TeeLogHeader` for v2**

Replace the declaration, constants, `Render`, and the parsing internals in
`src/DotnetTokenKiller.Domain/Tee/TeeLogHeader.cs`. `StripHeader` and `Flatten` stay exactly as they are.

```csharp
/// <param name="ExitCode">
/// The producing command's exit code, or <see langword="null"/> when the run has not finished.
/// This is the sole in-memory representation of completion: <see cref="Status"/> derives from it,
/// so the two can never disagree.
/// </param>
public sealed record TeeLogHeader(
    string CommandLine,
    string ProjectPath,
    int? ExitCode,
    RunSource Source,
    DateTimeOffset TimestampUtc)
{
    /// <summary>The first line of a v2 header.</summary>
    public const string VersionLine = "# dtk-log v2";

    /// <summary>The first line of a v1 header, still accepted when reading.</summary>
    public const string LegacyVersionLine = "# dtk-log v1";

    /// <summary>The line separating the header from the raw body.</summary>
    public const string Delimiter = "---";

    /// <summary>Width of the status value, sized to the longest word it can hold.</summary>
    private const int StatusFieldWidth = 8;

    /// <summary>Width of the exit value, sized to <c>-2147483648</c>.</summary>
    private const int ExitFieldWidth = 11;

    private const string RunningText = "running";
    private const string CompleteText = "complete";

    private const string CommandKey = "command";
    private const string CwdKey = "cwd";
    private const string ExitKey = "exit";
    private const string SourceKey = "source";
    private const string StatusKey = "status";
    private const string UtcKey = "utc";

    /// <summary>Whether the run this log came from finished.</summary>
    public TeeLogStatus Status => ExitCode is null ? TeeLogStatus.Running : TeeLogStatus.Complete;

    /// <summary>
    /// Renders the status and exit lines, which sit last and adjacent so finalizing a log is one
    /// contiguous overwrite at a known byte offset.
    /// </summary>
    /// <param name="exitCode">The exit code, or <see langword="null"/> for a run still in flight.</param>
    /// <returns>
    /// Both lines including their trailing line feeds. The length is identical for every input —
    /// the in-place overwrite depends on it, and <c>RenderStatusAndExit_ProducesTheSameLength…</c>
    /// guards it.
    /// </returns>
    public static string RenderStatusAndExit(int? exitCode)
    {
        var status = (exitCode is null ? RunningText : CompleteText).PadRight(StatusFieldWidth);
        var exit = (exitCode?.ToString(CultureInfo.InvariantCulture) ?? "-").PadRight(ExitFieldWidth);
        return $"# {StatusKey}: {status}\n# {ExitKey}:   {exit}\n";
    }

    /// <summary>Renders the header, including the trailing delimiter line.</summary>
    /// <returns>The header text; the body is appended directly after it.</returns>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append(VersionLine).Append('\n')
            .Append("# ").Append(CommandKey).Append(": ").Append(Flatten(CommandLine)).Append('\n')
            .Append("# ").Append(CwdKey).Append(": ").Append(Flatten(ProjectPath)).Append('\n')
            .Append("# ").Append(SourceKey).Append(": ").Append(Source.ToString()).Append('\n')
            .Append("# ").Append(UtcKey).Append(": ")
            .Append(TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(RenderStatusAndExit(ExitCode))
            .Append(Delimiter).Append('\n');
        return sb.ToString();
    }
```

Replace the body of `TryParse` between the version check and the final construction with:

```csharp
        var lines = text.Split('\n');
        var version = lines[0].TrimEnd('\r');
        if (version != VersionLine && version != LegacyVersionLine)
        {
            return false;
        }

        string? command = null;
        string? cwd = null;
        string? exit = null;
        string? source = null;
        string? status = null;
        string? utc = null;
        var sawDelimiter = false;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line == Delimiter)
            {
                sawDelimiter = true;
                break;
            }

            if (!line.StartsWith("# ", StringComparison.Ordinal))
            {
                return false;
            }

            var rest = line[2..];
            var separator = rest.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                return false;
            }

            var key = rest[..separator];
            var value = rest[(separator + 1)..].TrimStart(' ');
            switch (key)
            {
                case CommandKey:
                    command = value;
                    break;
                case CwdKey:
                    cwd = value;
                    break;
                case ExitKey:
                    // Trimmed at both ends: this field is padded to a fixed width so it can be
                    // overwritten in place. The others are not trimmed at the end, because Flatten
                    // can legitimately leave a trailing space in a command line.
                    exit = value.TrimEnd(' ');
                    break;
                case SourceKey:
                    source = value;
                    break;
                case StatusKey:
                    status = value.TrimEnd(' ');
                    break;
                case UtcKey:
                    utc = value;
                    break;
                default:
                    return false;
            }
        }

        if (!sawDelimiter || command is null || cwd is null || exit is null || source is null || utc is null)
        {
            return false;
        }

        if (!TryParseExit(exit, status, out var exitCode))
        {
            return false;
        }

        if (!Enum.TryParse<RunSource>(source, ignoreCase: false, out var runSource) ||
            !DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return false;
        }

        header = new TeeLogHeader(command, cwd, exitCode, runSource, timestamp);
        return true;
    }

    /// <summary>Resolves the exit field against the status field, rejecting any disagreement.</summary>
    /// <param name="exit">The raw exit value: a decimal integer, or <c>-</c> for a run in flight.</param>
    /// <param name="status">The raw status value, or <see langword="null"/> in a v1 header.</param>
    /// <param name="exitCode">The parsed exit code, or <see langword="null"/> when still running.</param>
    /// <returns><see langword="true"/> when the two fields agree and parse.</returns>
    private static bool TryParseExit(string exit, string? status, out int? exitCode)
    {
        exitCode = null;

        // v1 had no status line and could only be written after the process exited.
        if (status is null)
        {
            if (!int.TryParse(exit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v1Exit))
            {
                return false;
            }

            exitCode = v1Exit;
            return true;
        }

        if (status == RunningText)
        {
            return exit == "-";
        }

        if (status != CompleteText)
        {
            return false;
        }

        if (!int.TryParse(exit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedExit))
        {
            return false;
        }

        exitCode = parsedExit;
        return true;
    }
```

- [ ] **Step 5: Fix the call sites the nullable change broke**

`int` converts to `int?` implicitly, so construction sites still compile. Reads do not. Update these two, temporarily, so the solution builds — Task 7 gives them their final form:

`src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs:36` — `TeeMode.Failures => header.ExitCode != 0` becomes `TeeMode.Failures => header.ExitCode is not 0`.

`src/DotnetTokenKiller.Cli/Formatting/TeeLogRenderer.cs:36` and `:67` — replace `entry.Header.ExitCode.ToString(CultureInfo.InvariantCulture)` with `entry.Header.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "incomplete"` at line 36, and at line 67 with:

```csharp
        var exit = entry.Header is null
            ? "exit unknown"
            : entry.Header.ExitCode is { } code
                ? $"exit {code.ToString(CultureInfo.InvariantCulture)}"
                : "incomplete";
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. `FileTeeServiceTests` and `FileTeeLogStoreTests` still pass — they write and read through the same type, so the version bump is invisible to them.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Tee/TeeLogStatus.cs \
        src/DotnetTokenKiller.Domain/Tee/TeeLogHeader.cs \
        src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs \
        src/DotnetTokenKiller.Cli/Formatting/TeeLogRenderer.cs \
        tests/DotnetTokenKiller.Domain.Tests/Tee/TeeLogHeaderTests.cs
git commit -m "feat: tee log header v2 with a running status"
```

---

### Task 2: `ITeeSession` and `FileTeeSession`

The session owns an open `FileStream` for the run's duration. This is where the durability guarantee actually lives: the header is on disk before the child starts, and lines land as they arrive.

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Tee/ITeeSession.cs`
- Create: `src/DotnetTokenKiller.Domain/Tee/NullTeeSession.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Tee/Utf8Text.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeSession.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs` (delegate its private `TruncateToUtf8Bytes` to `Utf8Text`)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeSessionTests.cs`

**Interfaces:**
- Consumes: `TeeLogHeader.Render()`, `TeeLogHeader.RenderStatusAndExit(int?)`, `TeeLogStatus` from Task 1.
- Produces: `ITeeSession { TextWriter Writer { get; } Task<string?> FinalizeAsync(int exitCode, CancellationToken ct = default) }` extending `IAsyncDisposable`; `NullTeeSession.Instance`; `public sealed class FileTeeSession : ITeeSession` with constructor `FileTeeSession(FileStream stream, string filePath, long statusRegionOffset, int statusRegionLength, long maxBodyBytes, long minBodyBytes, bool keepOnlyOnFailure)`; `internal static class Utf8Text { public static string TruncateToUtf8Bytes(string text, long maxBytes) }`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeSessionTests.cs`:

```csharp
using System.Text;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class FileTeeSessionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-session-test-{Guid.NewGuid()}");

    public FileTeeSessionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static TeeLogHeader RunningHeader() => new(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        null,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

    /// <summary>Opens a session over a real file, mirroring what FileTeeService.BeginAsync does.</summary>
    private (FileTeeSession Session, string Path) CreateSut(
        long maxBodyBytes = 1_048_576L,
        long minBodyBytes = 500,
        bool keepOnlyOnFailure = false)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var header = RunningHeader();
        var rendered = header.Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var bytes = Encoding.UTF8.GetBytes(rendered);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        var session = new FileTeeSession(stream, path, offset, Encoding.UTF8.GetByteCount(region),
            maxBodyBytes, minBodyBytes, keepOnlyOnFailure);
        return (session, path);
    }

    [Fact]
    public async Task AbandonedSession_LeavesAReadableRunningLog()
    {
        // This is the whole feature. A dtk process killed by SIGKILL never reaches FinalizeAsync,
        // so what this test asserts is exactly what survives a tool-call timeout.
        var (session, path) = CreateSut();

        await session.Writer.WriteLineAsync("first line".AsMemory(), CancellationToken.None);
        await session.Writer.FlushAsync(CancellationToken.None);

        // Deliberately no FinalizeAsync and no DisposeAsync.
        var text = await File.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Running);
        header.ExitCode.Should().BeNull();
        TeeLogHeader.StripHeader(text).Should().Contain("first line");
    }

    [Fact]
    public async Task FinalizeAsync_MarksTheLogComplete_WithoutChangingItsLength()
    {
        var (session, path) = CreateSut(minBodyBytes: 0);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);
        await session.Writer.FlushAsync(CancellationToken.None);
        var lengthBeforeFinalize = new FileInfo(path).Length;

        var hint = await session.FinalizeAsync(3);

        new FileInfo(path).Length.Should().Be(lengthBeforeFinalize);
        hint.Should().Contain(path);
        var text = await File.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Complete);
        header.ExitCode.Should().Be(3);
        TeeLogHeader.StripHeader(text).Should().Contain("body");
    }

    [Fact]
    public async Task FinalizeAsync_DeletesTheLog_WhenTheBodyIsBelowTheGuard()
    {
        var (session, path) = CreateSut(minBodyBytes: 500);
        await session.Writer.WriteLineAsync("tiny".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_DeletesTheLog_WhenOnlyFailuresAreKeptAndTheRunSucceeded()
    {
        var (session, path) = CreateSut(minBodyBytes: 0, keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_KeepsTheLog_WhenOnlyFailuresAreKeptAndTheRunFailed()
    {
        var (session, path) = CreateSut(minBodyBytes: 0, keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(1);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_StopsAppending_OnceTheByteCapIsReached()
    {
        var (session, path) = CreateSut(maxBodyBytes: 32, minBodyBytes: 0);

        for (var i = 0; i < 20; i++)
        {
            await session.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        }

        await session.FinalizeAsync(0);
        var body = TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().BeLessThanOrEqualTo(32);
    }

    [Fact]
    public async Task Writer_StripsAnsiEscapes()
    {
        var (session, path) = CreateSut(minBodyBytes: 0);

        await session.Writer.WriteLineAsync("[31mred[0m".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path)).Should().Be("red\n");
    }

    [Fact]
    public async Task Writer_UsesLineFeedEndings()
    {
        // The tee file is read back by dtk log and must not gain CRLF on Windows.
        var (session, path) = CreateSut(minBodyBytes: 0);

        await session.Writer.WriteLineAsync("a".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("b".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path)).Should().Be("a\nb\n");
    }

    [Fact]
    public async Task Writer_DoesNotThrow_WhenTheUnderlyingStreamIsBroken()
    {
        // A sink that throws inside ProcessCommandRunner.PumpAsync would fail the user's build.
        // That is the one way streaming can violate "tee errors never surface", so it is latched.
        var (session, _) = CreateSut(minBodyBytes: 0);
        await session.DisposeAsync();

        var act = async () =>
        {
            await session.Writer.WriteLineAsync("after disposal".AsMemory(), CancellationToken.None);
            await session.Writer.FlushAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsNull_WhenTheSessionIsAlreadyDisposed()
    {
        var (session, _) = CreateSut(minBodyBytes: 0);
        await session.DisposeAsync();

        (await session.FinalizeAsync(0)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~FileTeeSessionTests"`
Expected: compile error — `FileTeeSession` and `ITeeSession` do not exist.

- [ ] **Step 3: Create the Domain abstractions**

Create `src/DotnetTokenKiller.Domain/Tee/ITeeSession.cs`:

```csharp
namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// A tee log being written while its run is still in progress.
/// </summary>
/// <remarks>
/// The log exists on disk from the moment the session is created, which is what makes it survive a
/// dtk process that is killed rather than cancelled — dtk's cancellation token never fires in
/// production, so no handler-based approach can offer the same guarantee.
/// </remarks>
public interface ITeeSession : IAsyncDisposable
{
    /// <summary>
    /// Receives the run's output as it arrives. Never throws: a failing sink would otherwise
    /// propagate out of the output pump and fail the user's command.
    /// </summary>
    TextWriter Writer { get; }

    /// <summary>Records the exit code and applies the retention rules.</summary>
    /// <param name="exitCode">The producing command's exit code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A hint naming the log file, or <see langword="null"/> when no log was kept — either because
    /// the retention rules discarded it, or because writing it failed.
    /// </returns>
    Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default);
}
```

Create `src/DotnetTokenKiller.Domain/Tee/NullTeeSession.cs`:

```csharp
namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// A session that writes nothing, used when tee is switched off or could not be opened.
/// </summary>
/// <remarks>
/// Returning this rather than null keeps every call site free of a "is tee on?" branch, which is
/// what stops the two run paths drifting apart.
/// </remarks>
public sealed class NullTeeSession : ITeeSession
{
    /// <summary>The shared instance; the type holds no state.</summary>
    public static readonly NullTeeSession Instance = new();

    private NullTeeSession()
    {
    }

    /// <inheritdoc/>
    public TextWriter Writer => TextWriter.Null;

    /// <inheritdoc/>
    public Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 4: Extract the UTF-8 truncation helper**

Create `src/DotnetTokenKiller.Infrastructure/Tee/Utf8Text.cs` by moving `TruncateToUtf8Bytes` verbatim out of `FileTeeService` (including its comment), changing only the class and accessibility:

```csharp
namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>UTF-8 helpers shared by the tee writer and the streaming session.</summary>
internal static class Utf8Text
{
    /// <summary>Cuts text to a UTF-8 byte budget on a code-point boundary.</summary>
    /// <param name="text">The text to cut.</param>
    /// <param name="maxBytes">The budget in UTF-8 bytes.</param>
    /// <returns>The longest prefix of <paramref name="text"/> fitting the budget.</returns>
    public static string TruncateToUtf8Bytes(string text, long maxBytes)
    {
        // MaxFileSizeBytes is a byte budget; slicing the string by char count could overshoot the cap
        // (multi-byte runes) or split a rune and emit U+FFFD. Cut on a UTF-8 code-point boundary instead.
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        // Walk runes and stop before the budget is exceeded rather than materializing the whole
        // string as a byte[] — captured output can be very large, and that allocation is the OOM
        // risk the tee path swallows (silently dropping the log). Slicing on a rune boundary also
        // guarantees we never split a multi-byte sequence.
        var chars = 0;
        var runeBytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (runeBytes + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            runeBytes += rune.Utf8SequenceLength;
            chars += rune.Utf16SequenceLength;
        }

        return text[..chars];
    }
}
```

Add `using System.Text;` at the top. In `FileTeeService.cs`, delete the private `TruncateToUtf8Bytes` method and change its single call site to `Utf8Text.TruncateToUtf8Bytes(rawOutput, teeConfig.MaxFileSizeBytes)`.

- [ ] **Step 5: Create `FileTeeSession`**

Create `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeSession.cs`:

```csharp
using System.Text;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>A tee log written incrementally to an open file for the duration of one run.</summary>
/// <remarks>
/// Public rather than internal because <c>DotnetTokenKiller.Infrastructure</c> grants no
/// <c>InternalsVisibleTo</c> and this type carries the durability guarantee its tests exist to prove.
/// </remarks>
public sealed class FileTeeSession : ITeeSession
{
    private readonly bool _keepOnlyOnFailure;
    private readonly long _maxBodyBytes;
    private readonly long _minBodyBytes;
    private readonly string _filePath;
    private readonly SessionWriter _writer;
    private readonly int _statusRegionLength;
    private readonly long _statusRegionOffset;
    private readonly FileStream _stream;
    private bool _disposed;

    /// <summary>Initializes a session over a stream whose header has already been written.</summary>
    /// <param name="stream">The open log file, positioned at the end of the header.</param>
    /// <param name="filePath">The log's path, used for the hint and for deletion.</param>
    /// <param name="statusRegionOffset">Byte offset of the status/exit region within the file.</param>
    /// <param name="statusRegionLength">Byte length of that region.</param>
    /// <param name="maxBodyBytes">The body's byte budget; appends stop once it is reached.</param>
    /// <param name="minBodyBytes">Bodies smaller than this are discarded when the run completes.</param>
    /// <param name="keepOnlyOnFailure">Whether a successful run's log is discarded.</param>
    public FileTeeSession(
        FileStream stream,
        string filePath,
        long statusRegionOffset,
        int statusRegionLength,
        long maxBodyBytes,
        long minBodyBytes,
        bool keepOnlyOnFailure)
    {
        _stream = stream;
        _filePath = filePath;
        _statusRegionOffset = statusRegionOffset;
        _statusRegionLength = statusRegionLength;
        _maxBodyBytes = maxBodyBytes;
        _minBodyBytes = minBodyBytes;
        _keepOnlyOnFailure = keepOnlyOnFailure;
        _writer = new SessionWriter(stream, maxBodyBytes);
    }

    /// <inheritdoc/>
    public TextWriter Writer => _writer;

    /// <inheritdoc/>
    public async Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if ((_keepOnlyOnFailure && exitCode == 0) || _writer.BodyBytesWritten < _minBodyBytes)
            {
                await DisposeAsync().ConfigureAwait(false);
                File.Delete(_filePath);
                return null;
            }

            var region = Encoding.UTF8.GetBytes(TeeLogHeader.RenderStatusAndExit(exitCode));
            if (region.Length != _statusRegionLength)
            {
                // Unreachable while RenderStatusAndExit pads to fixed widths; writing a
                // differently-sized region here would overwrite the delimiter and make every
                // completed log unparseable, so refuse rather than corrupt.
                await DisposeAsync().ConfigureAwait(false);
                return null;
            }

            _stream.Seek(_statusRegionOffset, SeekOrigin.Begin);
            await _stream.WriteAsync(region, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await DisposeAsync().ConfigureAwait(false);
            return $"[full output: {_filePath}]";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
            await DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.MarkBroken();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The sink handed to the output pump: strips ANSI, enforces the byte budget, serialises the
    /// two concurrent pumps, and swallows IO failures so a broken log cannot fail the run.
    /// </summary>
    private sealed class SessionWriter(FileStream stream, long maxBodyBytes) : TextWriter
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _broken;

        public long BodyBytesWritten { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        /// <summary>
        /// Forced to a line feed so the log matches the repo's LF-only policy, and so the text the
        /// pump accumulates for the filter is identical on every platform.
        /// </summary>
        public override string NewLine
        {
            get => "\n";
            set => _ = value;
        }

        public void MarkBroken() => _broken = true;

        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_broken)
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var remaining = maxBodyBytes - BodyBytesWritten;
                if (remaining <= 0)
                {
                    return;
                }

                var line = Utf8Text.TruncateToUtf8Bytes(
                    AnsiStrip.Strip(buffer.ToString()) + "\n", remaining);
                var bytes = Encoding.UTF8.GetBytes(line);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                BodyBytesWritten += bytes.Length;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: a throwing sink would propagate out of the output pump and fail the
                // user's command, which is the one way streaming can break "tee errors never surface".
                _broken = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_broken)
            {
                return;
            }

            try
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: see WriteLineAsync.
                _broken = true;
            }
        }

        public override Task FlushAsync() => FlushAsync(CancellationToken.None);

        public override void Write(char value)
        {
            // The pump only ever calls WriteLineAsync. Implemented because TextWriter requires it;
            // routing it through the async path would deadlock.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gate.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
```

`AnsiStrip` lives in `DotnetTokenKiller.Application.Helpers` today. Infrastructure cannot reference Application, so **move `AnsiStrip` to `DotnetTokenKiller.Domain/Text/AnsiStrip.cs`** (keeping the class `public static`), update the `using` in `FilteredOutputPipeline.cs`, `PassthroughRunUseCase.cs`, and `tests/DotnetTokenKiller.Application.Tests/Helpers/AnsiStripTests.cs`, and move that test file to `tests/DotnetTokenKiller.Domain.Tests/Text/AnsiStripTests.cs`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS, including the pre-existing `AnsiStripTests` in their new home.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Tee/ITeeSession.cs \
        src/DotnetTokenKiller.Domain/Tee/NullTeeSession.cs \
        src/DotnetTokenKiller.Domain/Text/AnsiStrip.cs \
        src/DotnetTokenKiller.Infrastructure/Tee/Utf8Text.cs \
        src/DotnetTokenKiller.Infrastructure/Tee/FileTeeSession.cs \
        src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs \
        src/DotnetTokenKiller.Application src/DotnetTokenKiller.Cli tests/
git add -u
git commit -m "feat: add an incremental tee session"
```

---

### Task 3: `ITeeService.BeginAsync`

Adds the factory alongside the existing `TeeAndHintAsync` so the solution stays green; Task 5 removes the old method once every consumer has moved.

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Tee/ITeeService.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs`

**Interfaces:**
- Consumes: `ITeeSession`, `NullTeeSession.Instance`, `FileTeeSession` from Task 2.
- Produces: `Task<ITeeSession> ITeeService.BeginAsync(string commandSlug, TeeLogHeader provisional, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs`:

```csharp
private static TeeLogHeader RunningHeader() => new(
    "dotnet build MyApp.slnx",
    "/home/user/projects/MyApp",
    null,
    RunSource.Run,
    new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

[Fact]
public async Task BeginAsync_WritesTheHeaderBeforeAnyOutputArrives()
{
    var sut = CreateSut(new TeeConfig(TeeMode.Always));

    await using var session = await sut.BeginAsync("build", RunningHeader());

    var text = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
    TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
    header.Status.Should().Be(TeeLogStatus.Running);
}

[Fact]
public async Task BeginAsync_OpensASession_EvenInFailuresMode()
{
    // The exit code is unknown at this point, so the mode cannot be applied yet. Deciding
    // early would mean never writing a log for a run that turns out to fail.
    var sut = CreateSut(new TeeConfig(TeeMode.Failures));

    await using var session = await sut.BeginAsync("build", RunningHeader());

    Directory.GetFiles(_tempDir).Should().ContainSingle();
}

[Fact]
public async Task BeginAsync_ReturnsANullSession_WhenTeeIsOff()
{
    var sut = CreateSut(new TeeConfig(TeeMode.Off));

    await using var session = await sut.BeginAsync("build", RunningHeader());

    session.Should().BeOfType<NullTeeSession>();
    (await session.FinalizeAsync(1)).Should().BeNull();
}

[Fact]
public async Task BeginAsync_RotatesOldLogs()
{
    var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFiles: 2));
    Directory.CreateDirectory(_tempDir);
    await File.WriteAllTextAsync(Path.Combine(_tempDir, "1000_a_build.log"), "old");
    await File.WriteAllTextAsync(Path.Combine(_tempDir, "2000_b_build.log"), "old");

    await using var session = await sut.BeginAsync("build", RunningHeader());

    Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(2);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~FileTeeServiceTests"`
Expected: compile error — `BeginAsync` does not exist on `FileTeeService`.

- [ ] **Step 3: Add `BeginAsync` to the interface**

Add to `src/DotnetTokenKiller.Domain/Tee/ITeeService.cs`:

```csharp
    /// <summary>Opens a log for a run that is about to start.</summary>
    /// <param name="commandSlug">A short identifier for the command, used in the file name.</param>
    /// <param name="provisional">
    /// The header to write immediately. Its exit code must be <see langword="null"/>; the real one
    /// is supplied to <see cref="ITeeSession.FinalizeAsync"/> when the run ends.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The session, or a <see cref="NullTeeSession"/> when tee is off or the log could not be
    /// opened. Never null, and never throws.
    /// </returns>
    Task<ITeeSession> BeginAsync(
        string commandSlug,
        TeeLogHeader provisional,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it in `FileTeeService`**

Add to `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs`:

```csharp
    /// <inheritdoc/>
    public async Task<ITeeSession> BeginAsync(
        string commandSlug,
        TeeLogHeader provisional,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provisional);
        try
        {
            var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            var teeConfig = config.Tee;

            // Failures mode is deliberately not consulted here: the exit code does not exist yet.
            // The log is written provisionally and discarded in FinalizeAsync if the run succeeded.
            if (teeConfig.Mode == TeeMode.Off)
            {
                return NullTeeSession.Instance;
            }

            var teeDir = TeeDirectoryResolver.Resolve(teeConfig, teeDirOverride);
            Directory.CreateDirectory(teeDir);
            RestrictToOwnerOnly(teeDir);
            RotateFiles(teeDir, teeConfig.MaxFiles);

            var fileName = TeeLogFileName.Build(
                provisional.TimestampUtc, Guid.NewGuid().ToString("N"), commandSlug);
            var filePath = Path.Combine(teeDir, fileName);

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.Read
            };

            // Keep the file owner-only (0600) on POSIX; UnixCreateMode is unsupported on Windows.
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var stream = new FileStream(filePath, options);
            var rendered = provisional.Render();
            var region = TeeLogHeader.RenderStatusAndExit(null);
            var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
            var regionOffset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
            var regionLength = Encoding.UTF8.GetByteCount(region);

            await stream.WriteAsync(Encoding.UTF8.GetBytes(rendered), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            return new FileTeeSession(stream, filePath, regionOffset, regionLength,
                teeConfig.MaxFileSizeBytes, minBodyBytes: 500, teeConfig.Mode == TeeMode.Failures);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
            return NullTeeSession.Instance;
        }
    }
```

Add `using DotnetTokenKiller.Domain.Tee;` if not already present (it is) and keep `using System.Text;`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Tee/ITeeService.cs \
        src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs \
        tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs
git commit -m "feat: open tee logs before the run starts"
```

---

### Task 4: `FanOutTextWriter`

The passthrough path must send each line to two places: the terminal, which the user is watching, and the tee session, which strips and records it.

**Files:**
- Create: `src/DotnetTokenKiller.Application/Helpers/FanOutTextWriter.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Helpers/FanOutTextWriterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed class FanOutTextWriter(TextWriter primary, TextWriter secondary) : TextWriter`.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Application.Tests/Helpers/FanOutTextWriterTests.cs`:

```csharp
using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class FanOutTextWriterTests
{
    [Fact]
    public async Task WriteLineAsync_ReachesBothWriters()
    {
        var primary = new StringWriter { NewLine = "\n" };
        var secondary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, secondary);

        await sut.WriteLineAsync("line".AsMemory(), CancellationToken.None);

        primary.ToString().Should().Be("line\n");
        secondary.ToString().Should().Be("line\n");
    }

    [Fact]
    public async Task WriteLineAsync_StillReachesTheTerminal_WhenTheSecondaryThrows()
    {
        // The secondary is the tee. A failing log must never cost the user their output.
        var primary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, new ThrowingWriter());

        var act = async () => await sut.WriteLineAsync("line".AsMemory(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        primary.ToString().Should().Be("line\n");
    }

    [Fact]
    public async Task FlushAsync_ReachesBothWriters()
    {
        var primary = new StringWriter { NewLine = "\n" };
        var secondary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, secondary);

        var act = async () => await sut.FlushAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");

        public override void Write(char value) => throw new IOException("disk full");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FanOutTextWriterTests"`
Expected: compile error — `FanOutTextWriter` does not exist.

- [ ] **Step 3: Implement it**

Create `src/DotnetTokenKiller.Application/Helpers/FanOutTextWriter.cs`:

```csharp
using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>Forwards each line to two writers.</summary>
/// <remarks>
/// Used on the passthrough path, where output must reach the terminal the user is watching and the
/// tee log at the same time. The secondary is treated as expendable: a tee that fails must not cost
/// the user the output they were waiting for.
/// </remarks>
/// <param name="primary">The writer whose failures propagate — the terminal.</param>
/// <param name="secondary">The writer whose failures are swallowed — the tee.</param>
internal sealed class FanOutTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
    /// <inheritdoc/>
    public override Encoding Encoding => primary.Encoding;

    /// <inheritdoc/>
    public override string NewLine
    {
        get => primary.NewLine;
        set => primary.NewLine = value;
    }

    /// <inheritdoc/>
    public override async Task WriteLineAsync(
        ReadOnlyMemory<char> buffer,
        CancellationToken cancellationToken = default)
    {
        await primary.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);

        try
        {
            await secondary.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
        }
    }

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await primary.FlushAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await secondary.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: see WriteLineAsync.
        }
    }

    /// <inheritdoc/>
    public override Task FlushAsync() => FlushAsync(CancellationToken.None);

    /// <inheritdoc/>
    public override void Write(char value) => primary.Write(value);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FanOutTextWriterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Helpers/FanOutTextWriter.cs \
        tests/DotnetTokenKiller.Application.Tests/Helpers/FanOutTextWriterTests.cs
git commit -m "feat: add a fan-out text writer for the passthrough tee"
```

---

### Task 5: Move the filtered path onto the session

`FilteredOutputPipeline` stops owning the tee and starts finalizing a session it is handed. That drops its `ITeeService` dependency and moves it to the two use cases that actually start runs.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs`
- Modify: `src/DotnetTokenKiller.Domain/Tee/ITeeService.cs` (delete `TeeAndHintAsync`)
- Modify: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs` (delete `TeeAndHintAsync`)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`, `.../FilteredOutputPipelineTests.cs`, `.../PipeFilterUseCaseTests.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs` (delete the `TeeAndHintAsync` tests; `BeginAsync` tests from Task 3 replace them)

**Interfaces:**
- Consumes: `ITeeService.BeginAsync`, `ITeeSession`, `NullTeeSession.Instance`.
- Produces: `FilteredOutputPipeline(ITracker tracker, TextWriter output, IConfigProvider configProvider)` — no `ITeeService`; `Task<int> ProcessAsync(FilteredOutputRequest request, ITeeSession session, CancellationToken ct = default)`; `FilteredRunUseCase(ICommandRunner commandRunner, ITeeService teeService, FilteredOutputPipeline pipeline, TextWriter output)`; `PipeFilterUseCase(FilteredOutputPipeline pipeline, ITeeService teeService, TextReader input)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`:

```csharp
[Fact]
public async Task RunAsync_OpensTheTeeBeforeRunningTheCommand()
{
    // Begin must precede the run: a log opened after the child starts is not on disk when a
    // tool-call timeout kills dtk, which is the whole point of the session.
    var callOrder = new List<string>();
    var session = Substitute.For<ITeeSession>();
    session.Writer.Returns(TextWriter.Null);
    _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
        .Returns(_ => { callOrder.Add("begin"); return session; });
    _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
        .Returns(_ => { callOrder.Add("run"); return new CommandResult("out", "", 0); });
    _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

    await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

    callOrder.Should().Equal("begin", "run");
}

[Fact]
public async Task RunAsync_OpensTheTeeWithARunningHeader()
{
    var session = Substitute.For<ITeeSession>();
    session.Writer.Returns(TextWriter.Null);
    _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
        .Returns(session);
    _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
        .Returns(new CommandResult("out", "", 0));
    _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

    await _sut.RunAsync(_filter, "dotnet", ListPackageArgs, 0);

    await _teeService.Received(1).BeginAsync(
        "list package",
        Arg.Is<TeeLogHeader>(h => h.ExitCode == null && h.Status == TeeLogStatus.Running),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunAsync_FinalizesTheSessionWithTheCommandExitCode()
{
    var session = Substitute.For<ITeeSession>();
    session.Writer.Returns(TextWriter.Null);
    _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
        .Returns(session);
    _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
        .Returns(new CommandResult("out", "", 7));
    _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

    await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

    await session.Received(1).FinalizeAsync(7, Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunAsync_DoesNotThrow_WhenFinalizingTheSessionFails()
{
    var session = Substitute.For<ITeeSession>();
    session.Writer.Returns(TextWriter.Null);
    session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new IOException("disk full"));
    _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
        .Returns(session);
    _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
        .Returns(new CommandResult("out", "", 0));
    _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

    var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

    await act.Should().NotThrowAsync();
}
```

Then update the whole existing file: replace every `_runner.RunCapturedAsync(...)` stub with the `RunStreamedAsync` five-argument form shown above, delete every `_teeService.TeeAndHintAsync(...)` stub, and change the constructor in the test's own constructor to:

```csharp
    public FilteredRunUseCaseTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var pipeline = new FilteredOutputPipeline(_tracker, TextWriter.Null, _configProvider);
        _sut = new FilteredRunUseCase(_runner, _teeService, pipeline, TextWriter.Null);
    }
```

In `FilteredOutputPipelineTests.cs`, change every construction to the three-argument form and every call to pass a session:

```csharp
        var pipeline = new FilteredOutputPipeline(_tracker, _output, _configProvider);
        // ...
        await pipeline.ProcessAsync(request, NullTeeSession.Instance);
```

Where a test asserted that the tee was written, assert on a substituted session instead:

```csharp
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[full output: /tee/x.log]");

        await pipeline.ProcessAsync(request, session);

        await session.Received(1).FinalizeAsync(request.ExitCode, Arg.Any<CancellationToken>());
```

In `PipeFilterUseCaseTests.cs`, add `private readonly ITeeService _teeService = Substitute.For<ITeeService>();`, stub `BeginAsync` to a substituted session with `Writer` returning `TextWriter.Null` exactly as the `FilteredRunUseCaseTests` constructor above does, and construct the subject as:

```csharp
        var pipeline = new FilteredOutputPipeline(_tracker, _output, _configProvider);
        _sut = new PipeFilterUseCase(pipeline, _teeService, new StringReader(input));
```

Add `using DotnetTokenKiller.Domain.Tee;` to both files if absent.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: compile errors — the constructors take a different number of arguments, and `BeginAsync` is not stubbed anywhere.

- [ ] **Step 3: Reshape `FilteredOutputPipeline`**

In `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs`:

Change the primary constructor to drop `ITeeService`:

```csharp
/// <param name="tracker">The tracking store.</param>
/// <param name="output">The text writer for user-facing output.</param>
/// <param name="configProvider">The configuration provider.</param>
public sealed class FilteredOutputPipeline(
    ITracker tracker,
    TextWriter output,
    IConfigProvider configProvider)
```

Change the signature and the tee call:

```csharp
    /// <summary>Filters the request's output, writes it, and records the run.</summary>
    /// <param name="request">The output and metadata to process.</param>
    /// <param name="session">
    /// The log opened for this run, finalized here. Callers with nothing to log pass
    /// <see cref="NullTeeSession.Instance"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request's exit code, unchanged.</returns>
    public async Task<int> ProcessAsync(
        FilteredOutputRequest request,
        ITeeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
```

Replace the `GetTeeHintAsync` call at line 48 with:

```csharp
        // Finalized before the raw-tail fallback is built, because the fallback embeds this hint.
        var logHint = await FinalizeTeeAsync(session, request.ExitCode, cancellationToken).ConfigureAwait(false);
```

Delete the `GetTeeHintAsync` method and replace it with:

```csharp
    private static async Task<string?> FinalizeTeeAsync(
        ITeeSession session,
        int exitCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await session.FinalizeAsync(exitCode, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
            return null;
        }
    }
```

Remove the now-unused `using DotnetTokenKiller.Domain.Tee;`? No — keep it: `ITeeSession` and `NullTeeSession` live there.

- [ ] **Step 4: Reshape `FilteredRunUseCase`**

Replace the class body of `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs`'s `RunAsync` (keeping `ResolveCommandSlug` untouched) and add `ITeeService` to the primary constructor:

```csharp
/// <param name="commandRunner">The command runner.</param>
/// <param name="teeService">Opens the log the run streams into.</param>
/// <param name="pipeline">The shared output-filtering pipeline.</param>
/// <param name="output">The text writer for user-facing output.</param>
public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITeeService teeService,
    FilteredOutputPipeline pipeline,
    TextWriter output)
```

```csharp
        var options = new OutputOptions(verbosityLevel, showLogHint, quiet).Normalized();
        var startTimestamp = Stopwatch.GetTimestamp();
        var commandSlug = ResolveCommandSlug(command, args);
        var displayCommandLine = args.Count > 0 ? $"{command} {string.Join(' ', args)}" : command;

        if (options.VerbosityLevel >= 1)
        {
            await output.WriteLineAsync($"$ {command} {string.Join(' ', args)}").ConfigureAwait(false);
        }

        // Opened before the child starts, so the log is already on disk if dtk is killed mid-run.
        // The timestamp is therefore the run's start, not its end; the filename derives from it, so
        // ordering is unaffected.
        var provisional = new TeeLogHeader(
            displayCommandLine,
            Environment.CurrentDirectory,
            null,
            RunSource.Run,
            DateTimeOffset.UtcNow);

        await using var session = await teeService
            .BeginAsync(commandSlug, provisional, cancellationToken).ConfigureAwait(false);

        // Both sinks are the same writer: it serialises the two concurrent pumps internally, and
        // interleaving stdout and stderr by arrival is a truer record than concatenating them.
        var result = await commandRunner
            .RunStreamedAsync(command, args, session.Writer, session.Writer, cancellationToken)
            .ConfigureAwait(false);

        var request = new FilteredOutputRequest(
            filter,
            result.StdOut + result.StdErr,
            result.ExitCode,
            commandSlug,
            displayCommandLine,
            RunSource.Run,
            options,
            startTimestamp);

        return await pipeline.ProcessAsync(request, session, cancellationToken).ConfigureAwait(false);
```

Add `using DotnetTokenKiller.Domain.Tee;`.

- [ ] **Step 5: Move `PipeFilterUseCase` onto the session**

In `src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs`, add `ITeeService teeService` as the second constructor parameter and replace the body after `ReadToEndAsync`:

```csharp
        // Piped input is read to completion before anything can be written, so this path gains no
        // durability. It uses the session API so the header format and the retention rules have a
        // single implementation rather than two that can drift.
        var provisional = new TeeLogHeader(
            $"dotnet {commandSlug}",
            Environment.CurrentDirectory,
            null,
            RunSource.Pipe,
            DateTimeOffset.UtcNow);

        await using var session = await teeService
            .BeginAsync(commandSlug, provisional, cancellationToken).ConfigureAwait(false);
        await session.Writer.WriteLineAsync(raw.AsMemory(), cancellationToken).ConfigureAwait(false);

        var request = new FilteredOutputRequest(
            filter,
            raw,
            exitCode,
            commandSlug,
            $"dotnet {commandSlug}",
            RunSource.Pipe,
            options.Normalized(),
            startTimestamp);

        return await pipeline.ProcessAsync(request, session, cancellationToken).ConfigureAwait(false);
```

Add `using DotnetTokenKiller.Domain.Tee;`.

- [ ] **Step 6: Delete `TeeAndHintAsync`**

Remove the method from `src/DotnetTokenKiller.Domain/Tee/ITeeService.cs` and from
`src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs`, along with `WriteOwnerOnlyAsync`, which only that method used. Delete the `TeeAndHintAsync_*` tests from `FileTeeServiceTests.cs`; the `BeginAsync` tests added in Task 3 cover the same ground.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. DI needs no change — `FilteredRunUseCase` and `PipeFilterUseCase` are registered by type and their new dependencies are already in the container.

- [ ] **Step 8: Commit**

```bash
git add -u
git commit -m "feat: stream filtered runs into an open tee log"
```

---

### Task 6: Tee measurable passthrough runs

Passthrough runs currently produce no log at all, so `dtk log` cannot reach them — the leftover flagged when `dtk log` shipped.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`
- Modify: `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs` (exists; add to it)

**Interfaces:**
- Consumes: `ITeeService.BeginAsync`, `FanOutTextWriter` from Task 4.
- Produces: `PassthroughRunUseCase(ICommandRunner commandRunner, ITracker? tracker, ITeeService teeService, TextWriter stdOut, TextWriter stdErr)`.

**Why `ITracker?` is nullable here.** `PassthroughEntryPoint` deliberately skips DI and the database when tracking is off — that is the whole reason it exists (see its remarks). Now that tee can be on while tracking is off, it needs to run the use case *without* a tracker. A null tracker is the smallest honest way to say "tracking is off"; the alternative is a six-member `NullTracker` implementing `GainSummary`/`CoverageSummary` queries that nothing will ever call. `PassthroughRunUseCase` is constructed only here — it is not DI-registered — so this widens no other call site.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`:

```csharp
[Fact]
public async Task RunAsync_TeesAMeasurableRun()
{
    var session = Substitute.For<ITeeSession>();
    session.Writer.Returns(TextWriter.Null);
    _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
        .Returns(session);
    _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
        .Returns(new CommandResult("out", "", 0));

    await _sut.RunAsync(DtkConfig.Default, "dotnet", ["publish"]);

    await session.Received(1).FinalizeAsync(0, Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunAsync_StreamsAndTees_WhenTrackingIsOffButTeeIsOn()
{
    // Keying the streamed path on tracking alone would silently produce no passthrough log for a
    // user who turned tracking off and left tee on.
    var config = DtkConfig.Default with
    {
        Tracking = DtkConfig.Default.Tracking with { Enabled = false },
        Tee = new TeeConfig(TeeMode.Always)
    };
    var session = Substitute.For<ITeeSession>();
    session.Writer.Returns(TextWriter.Null);
    _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
        .Returns(session);
    _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
        .Returns(new CommandResult("out", "", 0));

    await _sut.RunAsync(config, "dotnet", ["publish"]);

    await session.Received(1).FinalizeAsync(0, Arg.Any<CancellationToken>());
    await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunAsync_TakesTheCheapPath_WhenBothTrackingAndTeeAreOff()
{
    var config = DtkConfig.Default with
    {
        Tracking = DtkConfig.Default.Tracking with { Enabled = false },
        Tee = new TeeConfig(TeeMode.Off)
    };
    _runner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
        Arg.Any<CancellationToken>()).Returns(0);

    await _sut.RunAsync(config, "dotnet", ["publish"]);

    await _runner.Received(1).RunPassthroughAsync(Arg.Any<string>(),
        Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    await _teeService.DidNotReceive().BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunAsync_DoesNotTeeAnInteractiveRun()
{
    // run/watch keep their stdio attached to the terminal, so there is nothing to capture.
    _runner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
        Arg.Any<CancellationToken>()).Returns(0);

    await _sut.RunAsync(DtkConfig.Default, "dotnet", ["run"]);

    await _teeService.DidNotReceive().BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(),
        Arg.Any<CancellationToken>());
}
```

Add `private readonly ITeeService _teeService = Substitute.For<ITeeService>();` to the existing fixture and pass it as the third constructor argument wherever `_sut` is built. In the fixture's constructor, default `BeginAsync` to a substituted session so the tests that do not care about tee still run:

```csharp
        var defaultSession = Substitute.For<ITeeSession>();
        defaultSession.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(defaultSession);
```

Existing tests in this file that assert tracking behaviour keep passing `_tracker`; the new nullable parameter only affects `PassthroughEntryPoint`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~PassthroughRunUseCaseTests"`
Expected: compile error — the constructor takes four arguments, not five.

- [ ] **Step 3: Implement**

In `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`, add `ITeeService teeService` as the third primary-constructor parameter (documented as `/// <param name="teeService">Opens the log a measured run streams into.</param>`) and replace `RunAsync`'s body after the null checks:

```csharp
        // Tee and tracking are configured independently, so either one is reason enough to capture.
        var teeEnabled = config.Tee.Mode != TeeMode.Off;
        if (!config.Tracking.Enabled && !teeEnabled)
        {
            // Nothing to measure and nothing to log, so leave the child's stdio attached to the
            // terminal exactly as it is today — colour included.
            return await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
        }

        var commandName = PassthroughSubcommands.CommandName(dotnetArgs);
        var stopwatch = Stopwatch.StartNew();

        if (!PassthroughSubcommands.IsMeasurable(dotnetArgs))
        {
            // Interactive: stdio stays attached, so there is no output to capture or tee.
            var passthroughExit = await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            await TrackAsync(commandName, null, config.Tracking.Tokenizer, stopwatch.Elapsed, passthroughExit,
                    RunOutcome.PassthroughUnmeasured, cancellationToken)
                .ConfigureAwait(false);
            return passthroughExit;
        }

        var provisional = new TeeLogHeader(
            $"{command} {string.Join(' ', dotnetArgs)}",
            Environment.CurrentDirectory,
            null,
            RunSource.Run,
            DateTimeOffset.UtcNow);

        await using var session = await teeService
            .BeginAsync(commandName, provisional, cancellationToken).ConfigureAwait(false);

        // The terminal takes the raw line so colour survives; the session strips ANSI itself.
        var outSink = new FanOutTextWriter(stdOut, session.Writer);
        var errSink = new FanOutTextWriter(stdErr, session.Writer);

        var result = await commandRunner
            .RunStreamedAsync(command, dotnetArgs, outSink, errSink, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        // The hint is discarded rather than printed: passthrough emits no dtk meta-output today,
        // and the log is reachable through `dtk log`.
        await session.FinalizeAsync(result.ExitCode, cancellationToken).ConfigureAwait(false);

        // The child's exit code above is already captured before any of this runs, so a throw from
        // here on — including from stripping/tokenizing a very large captured output — cannot alter
        // what dtk returns; TrackAsync's try/catch covers stripping and estimation as well as the
        // store write.
        await TrackAsync(commandName, result.StdOut + result.StdErr, config.Tracking.Tokenizer,
                stopwatch.Elapsed, result.ExitCode, RunOutcome.PassthroughMeasured, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode;
```

Change the constructor's tracker parameter to `ITracker? tracker`, documented as:

```csharp
/// <param name="tracker">The tracking store, or <see langword="null"/> when tracking is off.</param>
```

and make `TrackAsync` return immediately when there is nothing to record — this is now the single place tracking is gated:

```csharp
        if (tracker is null)
        {
            return;
        }
```

as the first statement of `TrackAsync`, before the `try`.

Add `using DotnetTokenKiller.Application.Helpers;` and `using DotnetTokenKiller.Domain.Tee;`.

- [ ] **Step 4: Update the entry point's manual construction**

`src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs` deliberately skips DI. Its own `!config.Tracking.Enabled` early return would now also skip the tee, so it needs a third branch. Replace `RunAsync`'s body:

```csharp
        var configProvider = new JsonConfigProvider();
        var config = await configProvider.LoadAsync().ConfigureAwait(false);
        var runner = new ProcessCommandRunner();

        if (!config.Tracking.Enabled && config.Tee.Mode == TeeMode.Off)
        {
            // Nothing to record and nothing to log, so open neither the database nor a log file.
            return await runner.RunPassthroughAsync(command, dotnetArgs).ConfigureAwait(false);
        }

        var teeService = new FileTeeService(configProvider);

        if (!config.Tracking.Enabled)
        {
            // Tee on, tracking off: write the log, but still never open the database.
            var teeOnly = new PassthroughRunUseCase(
                runner, tracker: null, teeService, Console.Out, Console.Error);
            return await teeOnly.RunAsync(config, command, dotnetArgs).ConfigureAwait(false);
        }

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var tracker = TrackerFactory.Create(config);
#pragma warning restore CA2007
        var useCase = new PassthroughRunUseCase(runner, tracker, teeService, Console.Out, Console.Error);
        return await useCase.RunAsync(config, command, dotnetArgs).ConfigureAwait(false);
```

Add `using DotnetTokenKiller.Domain.Configuration;` (for `TeeMode`) and `using DotnetTokenKiller.Infrastructure.Tee;` (for `FileTeeService`). The single `JsonConfigProvider` is now kept in a local rather than discarded, because `FileTeeService` needs it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -u
git commit -m "feat: tee measurable passthrough runs"
```

---

### Task 7: Show incomplete runs in `dtk log`

Task 1 left `TeeLogRenderer` with a minimal nullable fix. This gives it its final form: a truncated build log must not read as a complete one.

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Formatting/TeeLogRenderer.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/LogCommandTests.cs`

**Interfaces:**
- Consumes: `TeeLogHeader.Status`, `TeeLogHeader.ExitCode` (`int?`).
- Produces: no new API.

- [ ] **Step 1: Write the failing tests**

`LogCommandTests` drives `LogCommand` against an in-memory `FakeStore` of `TeeLogEntry` values — it does not seed files. Follow that pattern. Its existing helpers are `At(int minute)`, `Cwd`, `Entry(minute, slug, cwd, exitCode)`, `FakeStore(Dictionary<string,string> bodies, params TeeLogEntry[] entries)` and `Create(store, teeMode)` returning `(command, console, writer)`.

Add to `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/LogCommandTests.cs`:

```csharp
    /// <summary>Builds an entry shaped exactly as an abandoned session leaves one on disk.</summary>
    private static TeeLogEntry IncompleteEntry(int minute, string slug, string commandLine) =>
        new($"/tee/{minute}_{slug}_incomplete.log",
            new TeeLogHeader(commandLine, Cwd, null, RunSource.Run, At(minute)),
            2048,
            At(minute),
            slug);

    [Fact]
    public async Task Run_MarksAnUnfinishedRunAsIncomplete()
    {
        var entry = IncompleteEntry(5, "build", "dotnet build MyApp.slnx");
        var bodies = new Dictionary<string, string> { [entry.FilePath] = "compiling...\n" };
        var (command, _, writer) = Create(new FakeStore(bodies, entry));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        var output = writer.ToString();
        output.Should().Contain("incomplete");
        output.Should().Contain("run did not finish");
        output.Should().Contain("compiling...");
    }

    [Fact]
    public async Task Run_List_ShowsIncompleteInsteadOfAnExitCode()
    {
        var (command, console, _) = Create(new FakeStore(
            new Dictionary<string, string>(),
            Entry(5, "build"),
            IncompleteEntry(9, "build", "dotnet build B.slnx")));

        await command.RunAsync(new LogCommandSettings { List = true }, CancellationToken.None);

        console.Output.Should().Contain("incomplete");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~LogCommandTests"`
Expected: `Run_List_ShowsIncompleteInsteadOfAnExitCode` PASSES already (Task 1 put `incomplete` in the list cell); `Run_MarksAnUnfinishedRunAsIncomplete` FAILS on the missing `run did not finish` note. A test that passes before the change is still worth keeping — it pins behaviour Task 1 introduced without a test of its own.

- [ ] **Step 3: Give the renderer its final form**

In `src/DotnetTokenKiller.Cli/Formatting/TeeLogRenderer.cs`, the list cell stays as Task 1 left it. In `RenderViewAsync`, after the file-path line and before the `summary` line, add:

```csharp
        if (entry.Header?.Status == TeeLogStatus.Running)
        {
            await output.WriteLineAsync(
                    "run did not finish — output ends where dtk was killed".AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
```

Add `using DotnetTokenKiller.Domain.Tee;` if the file does not already have it (it does).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. If `CliConfiguratorTests.Configure_LogHelp_MatchesSnapshot` fails, the help text is unchanged by this task — investigate rather than re-accepting the snapshot.

- [ ] **Step 5: Commit**

```bash
git add -u
git commit -m "feat: mark unfinished runs in dtk log"
```

---

### Task 8: Prove durability out of process

Every test so far simulates a kill by not calling `FinalizeAsync`. This one actually kills dtk. It is the only test that proves the feature end to end, and the most likely to be flaky, so it is a single case guarded to POSIX.

**Files:**
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/TeeDurabilityTests.cs`

**Interfaces:**
- Consumes: `IntegrationTestHelper`'s existing `DllPath` and `TestDataRoot`.
- Produces: `IntegrationTestHelper.StartDetached(string[] args, string isolatedDir)` returning `(Process Process, string TeeDir)`.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/TeeDurabilityTests.cs`:

```csharp
using DotnetTokenKiller.Domain.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class TeeDurabilityTests
{
    [Fact]
    public async Task KilledRun_LeavesAReadableLog()
    {
        if (OperatingSystem.IsWindows())
        {
            // Kill(entireProcessTree) on Windows is the case ProcessCommandRunner already needs a
            // taskkill backstop for; the guarantee under test is POSIX signal behaviour.
            return;
        }

        var isolatedDir = Path.Combine(Path.GetTempPath(), $"dtk-durability-{Guid.NewGuid():N}");
        var (process, teeDir) = IntegrationTestHelper.StartDetached(
            ["dotnet", "build", "samples/SampleApp.Warnings/SampleApp.Warnings.csproj"], isolatedDir);

        try
        {
            // The log exists from BeginAsync, before the child process even starts, so this waits
            // on the guarantee itself rather than on the build producing output.
            var logPath = await WaitForLogAsync(teeDir, TimeSpan.FromSeconds(30));
            logPath.Should().NotBeNull("dtk must open the tee log before running the command");

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();

            var text = await File.ReadAllTextAsync(logPath!);
            TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
            header.Status.Should().Be(TeeLogStatus.Running);
            header.CommandLine.Should().Contain("build");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
            if (Directory.Exists(isolatedDir))
            {
                Directory.Delete(isolatedDir, true);
            }
        }
    }

    private static async Task<string?> WaitForLogAsync(string teeDir, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(teeDir))
            {
                var files = Directory.GetFiles(teeDir, "*.log");
                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            await Task.Delay(50);
        }

        return null;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~TeeDurabilityTests"`
Expected: compile error — `StartDetached` does not exist.

- [ ] **Step 3: Add the helper**

Add to `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs`, matching the environment setup `RunProcessAsync` already performs:

```csharp
    /// <summary>
    /// Starts dtk without waiting for it, so a test can kill it mid-run.
    /// </summary>
    /// <param name="args">Arguments to pass to dtk.</param>
    /// <param name="isolatedDir">The per-test directory holding config, database, and tee logs.</param>
    /// <returns>The running process and the tee directory it was pointed at.</returns>
    public static (Process Process, string TeeDir) StartDetached(string[] args, string isolatedDir)
    {
        var teeDir = Path.Combine(isolatedDir, "tee");
        Directory.CreateDirectory(isolatedDir);

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(DllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // Same isolation set RunProcessAsync uses, so this never touches the developer's real state.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        psi.Environment["DTK_DB_PATH"] = Path.Combine(isolatedDir, "tracking.db");
        psi.Environment["DTK_TEE_DIR"] = teeDir;
        psi.Environment["DTK_CONFIG_PATH"] = Path.Combine(isolatedDir, "config.json");

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException("Failed to start dtk");
        return (process, teeDir);
    }
```

`using System.Diagnostics;` and `using System.Text;` are already present in this file.

Note: tee defaults to `TeeMode.Failures`, and this run never reports an exit code, so the config the test writes must set tee mode to `always` — or rely on the fact that `BeginAsync` opens the log regardless of mode (except `off`), which is exactly what `BeginAsync_OpensASession_EvenInFailuresMode` asserts. No config file is needed.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~TeeDurabilityTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole suite and the formatter**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```
Expected: build clean (warnings are errors), all tests pass, formatting unchanged.

- [ ] **Step 6: Update the gap analysis**

Add to `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md`, after the §4 body:

```markdown
> **Resolved 2026-07-29.** The tee is written during the run rather than after it, so a dtk process
> killed by Ctrl-C or a tool-call timeout leaves a readable log. Measurable passthrough runs are
> tee'd too, which closes the gap §5 left open. Two findings reshaped the work: `RunStreamedAsync`
> already existed, and dtk's cancellation token never fires in production — so durability had to
> come from the file already being on disk, not from a cancellation handler. A live progress signal
> remains out of scope; on the filtered path it is in direct opposition to filtering.
> See [the design](2026-07-29-streaming-tee-durability-design.md).
```

And in "Suggested sequencing":

```markdown
6. ~~§4 — stream the tee so a killed run keeps its log.~~
   **Done 2026-07-29** — see the resolution note above and
   [the design](2026-07-29-streaming-tee-durability-design.md).
```

Also change the spec's own `**Status:** Design` line to `**Status:** Implemented — see [the plan](../plans/2026-07-29-streaming-tee-durability.md)`.

- [ ] **Step 7: Update user-facing docs**

In `README.md` and `docfx/articles/usage.md`, find the `dtk log` section and add:

```markdown
A run that dtk did not finish — because you pressed Ctrl-C, or an agent's tool call timed out —
still leaves a log. `dtk log` shows it with `incomplete` in place of an exit code and a note saying
the output ends where dtk was killed.
```

- [ ] **Step 8: Commit**

```bash
git add -u
git add tests/DotnetTokenKiller.Cli.IntegrationTests/TeeDurabilityTests.cs
git commit -m "test: prove a killed run keeps its tee log"
```

---

## Notes for the implementer

**Two behaviour changes are intentional and should not be "fixed" if a test surfaces them.**

1. *Line endings on the filtered path are normalized to `\n`.* `RunCapturedAsync` preserved the child's original endings; the line-based pump does not. Every filter splits on `'\n'` and calls `TrimEnd('\r')`, so this is safe, and the passthrough path has worked this way since coverage tracking shipped.

2. *stdout and stderr interleave by arrival in the tee body.* They were previously concatenated, stdout first. The text handed to the filter still comes from `result.StdOut + result.StdErr`, so filtering is unaffected; only the log's ordering changes, and interleaved is the truer record.

**The one thing that must not regress:** `FinalizeAsync` writes a region of exactly the same byte length it reserved. If that ever differs, the write runs past the `---` delimiter and *every* completed log becomes unparseable — logs would silently vanish from `dtk log` rather than fail loudly. `RenderStatusAndExit_ProducesTheSameLength_ForRunningAndAnyExitCode` and the length check inside `FinalizeAsync` are both guarding that, deliberately.
