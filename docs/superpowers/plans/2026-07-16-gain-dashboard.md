# `dtk gain` Dashboard Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the plain `dtk gain` table with an rtk-style dashboard: title/scope header, totals recap with efficiency meter, and a colored per-command table with K/M token units and impact bars.

**Architecture:** A new `TokenFormat` static helper (K/M + duration strings) and a `GainDashboardRenderer` static class in `DotnetTokenKiller.Cli/Formatting/` render the human display from `GainSummary`. The domain records `GainSummary`/`CommandGainDetail` gain an additive `TotalExecutionTime` component fed by a new `SUM(execution_time_ms)` in `SqliteTracker`'s summary query. `--json` and `--export csv` outputs keep raw numbers (JSON gains the exec-time field additively).

**Tech Stack:** .NET 10, Spectre.Console (+ Spectre.Console.Testing), xunit + FluentAssertions, Microsoft.Data.Sqlite.

**Spec:** `docs/superpowers/specs/2026-07-16-gain-dashboard-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is on — every analyzer warning fails the build.
- All number formatting uses `CultureInfo.InvariantCulture` explicitly (analyzer CA1305).
- File-scoped namespaces; `var` preferred; private fields `_camelCase`; LF line endings; 4-space indent; no trailing whitespace.
- XML doc comments (`/// <summary>`) on all types and public/internal members, matching existing files.
- Build with `dtk dotnet build DotnetTokenKiller.slnx`.
- Run targeted tests against the test **.csproj**, never the .slnx (known quirk: `dtk dotnet test <slnx> --filter` can falsely report "0 tests found"):
  `dtk dotnet test tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<TestClass>"`
- Format before final commit: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`.

---

### Task 1: `TokenFormat` helper (K/M tokens + compact durations)

**Files:**
- Create: `src/DotnetTokenKiller.Cli/Formatting/TokenFormat.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/TokenFormatTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class TokenFormat` in namespace `DotnetTokenKiller.Cli.Formatting` with:
  - `public static string Tokens(long value)` — `0→"0"`, `999→"999"`, `1000→"1.0K"`, `995397→"995.4K"`, `999999→"1.0M"` (K value that rounds to 1000.0 promotes to M), `2600000→"2.6M"`, `-1030→"-1.0K"`.
  - `public static string Duration(TimeSpan value)` — `<1s→"204ms"`, `<1min→"2.3s"`, `<1h→"38m12s"` (seconds zero-padded not required, minutes-seconds as `{m}m{ss:D2}s`), `≥1h→"2h05m"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/TokenFormatTests.cs`:

```csharp
using DotnetTokenKiller.Cli.Formatting;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Formatting;

public class TokenFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(88_400, "88.4K")]
    [InlineData(995_397, "995.4K")]
    [InlineData(999_999, "1.0M")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(2_600_000, "2.6M")]
    [InlineData(-500, "-500")]
    [InlineData(-1030, "-1.0K")]
    [InlineData(-2_600_000, "-2.6M")]
    public void Tokens_FormatsWithKAndMUnits(long value, string expected)
    {
        TokenFormat.Tokens(value).Should().Be(expected);
    }

    public static TheoryData<TimeSpan, string> DurationCases => new()
    {
        { TimeSpan.Zero, "0ms" },
        { TimeSpan.FromMilliseconds(204), "204ms" },
        { TimeSpan.FromSeconds(2.3), "2.3s" },
        { TimeSpan.FromSeconds(59), "59.0s" },
        { TimeSpan.FromSeconds((38 * 60) + 12), "38m12s" },
        { TimeSpan.FromMinutes(60), "1h00m" },
        { TimeSpan.FromMinutes(125), "2h05m" }
    };

    [Theory]
    [MemberData(nameof(DurationCases))]
    public void Duration_FormatsCompactly(TimeSpan value, string expected)
    {
        TokenFormat.Duration(value).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Verify the tests fail to compile**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `error CS0246: The type or namespace name 'TokenFormat' could not be found` (compile failure is the RED state; do not use `dtk dotnet test <slnx> --filter` for RED, see Global Constraints).

- [ ] **Step 3: Write the implementation**

Create `src/DotnetTokenKiller.Cli/Formatting/TokenFormat.cs`:

```csharp
using System.Globalization;

namespace DotnetTokenKiller.Cli.Formatting;

/// <summary>Formats token counts and durations compactly for the gain dashboard.</summary>
internal static class TokenFormat
{
    /// <summary>Formats a token count with K/M units, one decimal, invariant culture.</summary>
    /// <param name="value">The token count.</param>
    public static string Tokens(long value)
    {
        if (value < 0)
        {
            return "-" + Tokens(-value);
        }

        if (value < 1_000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value < 1_000_000)
        {
            var thousands = value / 1_000.0;

            // 999_999 rounds to "1000.0K"; promote to the next unit instead.
            return Math.Round(thousands, 1) >= 1_000.0
                ? Millions(value)
                : thousands.ToString("F1", CultureInfo.InvariantCulture) + "K";
        }

        return Millions(value);
    }

    /// <summary>Formats a duration with its two most significant units (e.g. 204ms, 2.3s, 38m12s, 2h05m).</summary>
    /// <param name="value">The duration.</param>
    public static string Duration(TimeSpan value)
    {
        if (value.TotalSeconds < 1)
        {
            return ((int)Math.Round(value.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture) + "ms";
        }

        if (value.TotalMinutes < 1)
        {
            return value.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
        }

        if (value.TotalHours < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes}m{value.Seconds:D2}s");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}h{value.Minutes:D2}m");
    }

    private static string Millions(long value)
        => (value / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + "M";
}
```

- [ ] **Step 4: Run the tests, verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj --filter "FullyQualifiedName~TokenFormatTests"`
Expected: PASS (19 test cases).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Cli/Formatting/TokenFormat.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/TokenFormatTests.cs
git commit -m "feat: add TokenFormat helper for K/M token units and compact durations"
```

---

### Task 2: Exec-time aggregation (Domain records + SqliteTracker)

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs`
- Modify: `src/DotnetTokenKiller.Domain/Tracking/CommandGainDetail.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (summary SQL at ~line 74, `ReadSummaryAsync` at ~line 279)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs` (add tests; `MakeRecord` helper at top of the class always records `TimeSpan.FromMilliseconds(500)`)
- Test: `tests/DotnetTokenKiller.Domain.Tests/GainSummaryTests.cs` (extend existing storage test)

**Interfaces:**
- Consumes: existing `commands` table column `execution_time_ms REAL NOT NULL`.
- Produces:
  - `GainSummary(..., IReadOnlyDictionary<string, CommandGainDetail> CommandDetails, TimeSpan TotalExecutionTime = default)` — new trailing component with default, so every existing constructor call still compiles.
  - `CommandGainDetail(..., CommandGainDetail? SuccessDetail = null, CommandGainDetail? FailureDetail = null, TimeSpan TotalExecutionTime = default)` — same pattern.
  - `SqliteTracker.GetSummaryAsync` fills both from `SUM(execution_time_ms)`.

- [ ] **Step 1: Write the failing tracker test**

Add to `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs` (inside the class, after `GetSummaryAsync_SplitsSuccessAndFailureIntoSubDetails`):

```csharp
    [Fact]
    public async Task GetSummaryAsync_AggregatesExecutionTime()
    {
        // MakeRecord always stamps 500ms per run.
        await _sut.RecordAsync(MakeRecord(command: "build", success: true));
        await _sut.RecordAsync(MakeRecord(command: "build", success: true));
        await _sut.RecordAsync(MakeRecord(command: "build", success: false));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1500));
        var build = summary.CommandDetails["build"];
        build.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1500));
        build.SuccessDetail!.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1000));
        build.FailureDetail!.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(500));
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `error CS1061: 'GainSummary' does not contain a definition for 'TotalExecutionTime'`.

- [ ] **Step 3: Add the domain components**

In `src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs`, replace the record declaration:

```csharp
/// <summary>Aggregated token-savings summary across one or more commands.</summary>
/// <param name="TotalCommands">Total number of commands run.</param>
/// <param name="TotalInputTokens">Sum of raw output tokens across all commands.</param>
/// <param name="TotalOutputTokens">Sum of filtered output tokens across all commands.</param>
/// <param name="TotalSavedTokens">Total tokens saved across all commands.</param>
/// <param name="AverageSavingsPercentage">Average savings percentage across command types.</param>
/// <param name="CommandDetails">Per-command breakdown.</param>
/// <param name="TotalExecutionTime">Total wall-clock execution time across all commands.</param>
public sealed record GainSummary(
    int TotalCommands,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalSavedTokens,
    double AverageSavingsPercentage,
    IReadOnlyDictionary<string, CommandGainDetail> CommandDetails,
    TimeSpan TotalExecutionTime = default);
```

In `src/DotnetTokenKiller.Domain/Tracking/CommandGainDetail.cs`, replace the record declaration:

```csharp
/// <summary>Token-savings detail for a single command type.</summary>
/// <param name="RunCount">Number of times this command was run.</param>
/// <param name="TotalInputTokens">Total raw output tokens.</param>
/// <param name="TotalOutputTokens">Total filtered output tokens.</param>
/// <param name="TotalSavedTokens">Total tokens saved.</param>
/// <param name="AverageSavingsPercentage">Average savings percentage.</param>
/// <param name="SuccessDetail">Detail for runs that exited with code 0, or <see langword="null"/> if not available.</param>
/// <param name="FailureDetail">Detail for runs that exited with a non-zero code, or <see langword="null"/> if none.</param>
/// <param name="TotalExecutionTime">Total wall-clock execution time across these runs.</param>
public sealed record CommandGainDetail(
    int RunCount,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalSavedTokens,
    double AverageSavingsPercentage,
    CommandGainDetail? SuccessDetail = null,
    CommandGainDetail? FailureDetail = null,
    TimeSpan TotalExecutionTime = default);
```

- [ ] **Step 4: Aggregate exec time in `SqliteTracker`**

In `GetSummaryAsync` (~line 74), add the sum to the SQL:

```csharp
        const string sql = """
                           SELECT command, success,
                                  COUNT(*) as run_count,
                                  SUM(input_tokens) as total_input,
                                  SUM(output_tokens) as total_output,
                                  SUM(saved_tokens) as total_saved,
                                  AVG(savings_percentage) as avg_pct,
                                  SUM(execution_time_ms) as total_ms
                           FROM commands
                           WHERE timestamp >= @since
                             AND (@path IS NULL OR project_path = @path)
                             AND (@cmd IS NULL OR command = @cmd)
                           GROUP BY command, success
                           ORDER BY command, success DESC
                           """;
```

In `ReadSummaryAsync` (~line 279), thread the new column through. The full updated method:

```csharp
    private static async Task<GainSummary> ReadSummaryAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var totalCommands = 0;
        long totalInput = 0;
        long totalOutput = 0;
        long totalSaved = 0;
        var totalMs = 0.0;

        // Accumulate per-command, per-status rows before building CommandGainDetail
        var grouped =
            new Dictionary<string, List<(bool Success, int RunCount, long SumInput, long SumOutput, long SumSaved, double
                AvgPct, double SumMs)>>(StringComparer.Ordinal);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var cmdName = reader.GetString(0);
            var success = reader.GetInt32(1) != 0;
            var runCount = reader.GetInt32(2);
            var sumInput = reader.GetInt64(3);
            var sumOutput = reader.GetInt64(4);
            var sumSaved = reader.GetInt64(5);
            var avgPct = reader.GetDouble(6);
            var sumMs = reader.GetDouble(7);

            totalCommands += runCount;
            totalInput += sumInput;
            totalOutput += sumOutput;
            totalSaved += sumSaved;
            totalMs += sumMs;

            if (!grouped.TryGetValue(cmdName, out var rows))
            {
                rows = [];
                grouped[cmdName] = rows;
            }

            rows.Add((success, runCount, sumInput, sumOutput, sumSaved, avgPct, sumMs));
        }

        var commandDetails = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal);
        foreach (var (cmdName, rows) in grouped)
        {
            CommandGainDetail? successDetail = null;
            CommandGainDetail? failureDetail = null;
            var totalRunsCmd = 0;
            long totalInputCmd = 0;
            long totalOutputCmd = 0;
            long totalSavedCmd = 0;
            var weightedPctSum = 0.0;
            var totalMsCmd = 0.0;

            foreach (var (success, runCount, sumInput, sumOutput, sumSaved, avgPct, sumMs) in rows)
            {
                var statusDetail = new CommandGainDetail(runCount, sumInput, sumOutput, sumSaved, avgPct,
                    TotalExecutionTime: TimeSpan.FromMilliseconds(sumMs));
                if (success)
                {
                    successDetail = statusDetail;
                }
                else
                {
                    failureDetail = statusDetail;
                }

                totalRunsCmd += runCount;
                totalInputCmd += sumInput;
                totalOutputCmd += sumOutput;
                totalSavedCmd += sumSaved;
                weightedPctSum += runCount * avgPct;
                totalMsCmd += sumMs;
            }

            // Run-count-weighted average of the per-status SQL AVG(savings_percentage) values,
            // keeping the same per-run-average semantics as SuccessDetail/FailureDetail.
            var avgPctCmd = totalRunsCmd > 0 ? weightedPctSum / totalRunsCmd : 0.0;
            commandDetails[cmdName] = new CommandGainDetail(totalRunsCmd, totalInputCmd, totalOutputCmd, totalSavedCmd,
                avgPctCmd, successDetail, failureDetail, TimeSpan.FromMilliseconds(totalMsCmd));
        }

        var averagePct = totalInput > 0 ? (double)totalSaved / totalInput * 100.0 : 0.0;
        return new GainSummary(totalCommands, totalInput, totalOutput, totalSaved, averagePct, commandDetails,
            TimeSpan.FromMilliseconds(totalMs));
    }
```

- [ ] **Step 5: Run tracker tests, verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqliteTrackerTests"`
Expected: PASS, including the new `GetSummaryAsync_AggregatesExecutionTime`.

- [ ] **Step 6: Extend the domain storage test**

In `tests/DotnetTokenKiller.Domain.Tests/GainSummaryTests.cs`, inside `GainSummary_stores_all_fields`, pass `TimeSpan.FromSeconds(3)` as the `TotalExecutionTime` argument when constructing the `GainSummary` and assert:

```csharp
        summary.TotalExecutionTime.Should().Be(TimeSpan.FromSeconds(3));
```

(Adapt to the test's existing constructor call — add the argument after `commandDetails`.)

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests/DotnetTokenKiller.Domain.Tests.csproj --filter "FullyQualifiedName~GainSummaryTests"`
Expected: PASS.

- [ ] **Step 7: Build the whole solution (JSON source-gen sanity)**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: SUCCESS — `GainSummaryJsonContext` regenerates with the new `TimeSpan` fields (System.Text.Json serializes `TimeSpan` natively as constant format, e.g. `"00:00:01.5000000"`).

- [ ] **Step 8: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs src/DotnetTokenKiller.Domain/Tracking/CommandGainDetail.cs src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs tests/DotnetTokenKiller.Domain.Tests/GainSummaryTests.cs
git commit -m "feat: aggregate execution time into gain summary"
```

---

### Task 3: `GainDashboardRenderer`

**Files:**
- Create: `src/DotnetTokenKiller.Cli/Formatting/GainDashboardRenderer.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/GainDashboardRendererTests.cs`

**Interfaces:**
- Consumes: `TokenFormat.Tokens(long)` / `TokenFormat.Duration(TimeSpan)` (Task 1); `GainSummary` with `TotalExecutionTime` (Task 2).
- Produces: `internal static class GainDashboardRenderer` in namespace `DotnetTokenKiller.Cli.Formatting` with `public static void Render(IAnsiConsole console, GainSummary summary, string scope)`. Callers must not pass an empty summary (`TotalCommands == 0`); the caller keeps the "No data yet" path.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/GainDashboardRendererTests.cs`:

```csharp
using DotnetTokenKiller.Cli.Formatting;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Formatting;

public class GainDashboardRendererTests
{
    private static GainSummary MakeSummary(
        double avgPct = 84.0,
        long totalSaved = 2100,
        TimeSpan? execTime = null)
    {
        var successDetail = new CommandGainDetail(3, 1500, 250, 1250, 83.3,
            TotalExecutionTime: TimeSpan.FromMilliseconds(1500));
        var failureDetail = new CommandGainDetail(2, 1000, 150, 850, 35.0,
            TotalExecutionTime: TimeSpan.FromMilliseconds(1000));
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(5, 2500, 400, totalSaved, avgPct, successDetail, failureDetail,
                TimeSpan.FromMilliseconds(2500))
        };
        return new GainSummary(5, 2500, 400, totalSaved, avgPct, details,
            execTime ?? TimeSpan.FromMilliseconds(2500));
    }

    private static TestConsole Render(GainSummary summary, string scope = "Global Scope")
    {
        var console = new TestConsole();
        GainDashboardRenderer.Render(console, summary, scope);
        return console;
    }

    [Fact]
    public void Render_WritesTitleWithScope()
    {
        var console = Render(MakeSummary(), "Project Scope, last 7 days");

        console.Output.Should().Contain("DTK Token Savings (Project Scope, last 7 days)");
        console.Output.Should().Contain("════");
    }

    [Fact]
    public void Render_WritesRecapBlockWithHumanUnits()
    {
        var console = Render(MakeSummary());

        console.Output.Should().Contain("Total commands:    5");
        console.Output.Should().Contain("Without tool:      2.5K");
        console.Output.Should().Contain("Used by tool:      400");
        console.Output.Should().Contain("Tokens saved:      2.1K (84.0%)");
        console.Output.Should().Contain("Total exec time:   2.5s (avg 500ms)");
    }

    [Fact]
    public void Render_EfficiencyMeter_FillsProportionally()
    {
        // 50% of a 24-char meter = 12 filled blocks.
        var console = Render(MakeSummary(avgPct: 50.0));

        console.Output.Should().Contain("Efficiency meter: " + new string('█', 12) + new string('░', 12) + " 50.0%");
    }

    [Fact]
    public void Render_EfficiencyMeter_ClampsOutOfRangePercentage()
    {
        var console = Render(MakeSummary(avgPct: 150.0));

        console.Output.Should().Contain(new string('█', 24) + " 150.0%");
    }

    [Fact]
    public void Render_Table_ShowsSplitRowsWithImpactBars()
    {
        var console = Render(MakeSummary());

        console.Output.Should().Contain("By Command");

        // Assert per line: the efficiency meter also contains long block runs, so a
        // whole-output Contain() would match the meter instead of the table bars.
        var okLine = console.Lines.Single(l => l.Contains("build (ok)", StringComparison.Ordinal));
        var failLine = console.Lines.Single(l => l.Contains("build (fail)", StringComparison.Ordinal));

        // ok row saved 1250 = max -> full 10-char bar; fail row 850/1250 -> 7 filled.
        okLine.Should().Contain(new string('█', 10));
        failLine.Should().Contain(new string('█', 7) + new string('░', 3));
    }

    [Fact]
    public void Render_Table_FormatsRowValuesWithUnits()
    {
        var console = Render(MakeSummary());

        console.Output.Should().Contain("1.5K"); // ok row Without Tool
        console.Output.Should().Contain("83.3%"); // ok row Avg%
        console.Output.Should().Contain("35.0%"); // fail row Avg%
    }

    [Fact]
    public void Render_Table_NegativeSavings_EmptyImpactBar()
    {
        var detail = new CommandGainDetail(2, 0, 1030, -1030, 0.0,
            TotalExecutionTime: TimeSpan.FromMilliseconds(200));
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["format"] = new(2, 0, 1030, -1030, 0.0, detail, null, TimeSpan.FromMilliseconds(200))
        };
        var summary = new GainSummary(2, 0, 1030, -1030, 0.0, details, TimeSpan.FromMilliseconds(200));

        var console = Render(summary);

        console.Output.Should().Contain("-1.0K");
        // Per line: the 0% efficiency meter is also a long run of '░'.
        var row = console.Lines.Single(l => l.Contains("format (ok)", StringComparison.Ordinal));
        row.Should().Contain(new string('░', 10)); // no fill for negative savings
        row.Should().NotContain("█");
    }
}
```

- [ ] **Step 2: Verify the tests fail to compile**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `error CS0246: ... 'GainDashboardRenderer' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/DotnetTokenKiller.Cli/Formatting/GainDashboardRenderer.cs`:

```csharp
using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;
using Spectre.Console;

namespace DotnetTokenKiller.Cli.Formatting;

/// <summary>Renders the rtk-style token-savings dashboard for <c>dtk gain</c>.</summary>
internal static class GainDashboardRenderer
{
    private const int MeterWidth = 24;
    private const int ImpactWidth = 10;
    private const int RuleWidth = 60;

    /// <summary>Writes the dashboard (header, recap, efficiency meter, per-command table).</summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="summary">The summary to render; must contain at least one command.</param>
    /// <param name="scope">Human-readable scope description (e.g. "Global Scope, last 7 days").</param>
    public static void Render(IAnsiConsole console, GainSummary summary, string scope)
    {
        RenderHeader(console, scope);
        RenderRecap(console, summary);
        RenderTable(console, summary);
    }

    private static void RenderHeader(IAnsiConsole console, string scope)
    {
        console.MarkupLine($"[bold cyan]DTK Token Savings ({scope.EscapeMarkup()})[/]");
        console.MarkupLine($"[grey]{new string('═', RuleWidth)}[/]");
        console.WriteLine();
    }

    private static void RenderRecap(IAnsiConsole console, GainSummary summary)
    {
        var pct = summary.AverageSavingsPercentage.ToString("F1", CultureInfo.InvariantCulture);
        var avgTime = summary.TotalCommands > 0
            ? TimeSpan.FromTicks(summary.TotalExecutionTime.Ticks / summary.TotalCommands)
            : TimeSpan.Zero;

        console.MarkupLine($"Total commands:    {summary.TotalCommands.ToString(CultureInfo.InvariantCulture)}");
        console.MarkupLine($"Without tool:      {TokenFormat.Tokens(summary.TotalInputTokens)}");
        console.MarkupLine($"Used by tool:      {TokenFormat.Tokens(summary.TotalOutputTokens)}");
        console.MarkupLine($"[green]Tokens saved:      {TokenFormat.Tokens(summary.TotalSavedTokens)} ({pct}%)[/]");
        console.MarkupLine(
            $"Total exec time:   {TokenFormat.Duration(summary.TotalExecutionTime)} (avg {TokenFormat.Duration(avgTime)})");

        var filled = (int)Math.Clamp(Math.Round(summary.AverageSavingsPercentage / 100.0 * MeterWidth), 0, MeterWidth);
        console.MarkupLine(
            $"Efficiency meter: [green]{new string('█', filled)}[/][grey]{new string('░', MeterWidth - filled)}[/] {pct}%");
        console.WriteLine();
    }

    private static void RenderTable(IAnsiConsole console, GainSummary summary)
    {
        var rows = new List<(string Label, string Color, CommandGainDetail Detail)>();
        foreach (var (cmd, detail) in summary.CommandDetails)
        {
            if (detail.SuccessDetail is { } sd)
            {
                rows.Add(($"{cmd} (ok)", "green", sd));
            }

            if (detail.FailureDetail is { } fd)
            {
                rows.Add(($"{cmd} (fail)", "red", fd));
            }
        }

        var maxSaved = rows.Count > 0 ? rows.Max(r => r.Detail.TotalSavedTokens) : 0;

        console.MarkupLine("[bold]By Command[/]");

        var table = new Table()
            .Border(TableBorder.Horizontal)
            .AddColumn("Command")
            .AddColumn(new TableColumn("Runs").RightAligned())
            .AddColumn(new TableColumn("Without Tool").RightAligned())
            .AddColumn(new TableColumn("Used by Tool").RightAligned())
            .AddColumn(new TableColumn("Saved").RightAligned())
            .AddColumn(new TableColumn("Avg%").RightAligned())
            .AddColumn("Impact");

        foreach (var (label, color, d) in rows)
        {
            table.AddRow(
                new Markup($"[{color}]{label.EscapeMarkup()}[/]"),
                new Text(d.RunCount.ToString(CultureInfo.InvariantCulture)),
                new Text(TokenFormat.Tokens(d.TotalInputTokens)),
                new Text(TokenFormat.Tokens(d.TotalOutputTokens)),
                new Markup(
                    $"[{SavedColor(d.TotalSavedTokens)}]{TokenFormat.Tokens(d.TotalSavedTokens)}[/]"),
                new Markup(
                    $"[{PctColor(d.AverageSavingsPercentage)}]{d.AverageSavingsPercentage.ToString("F1", CultureInfo.InvariantCulture)}%[/]"),
                new Markup(ImpactBar(d.TotalSavedTokens, maxSaved)));
        }

        console.Write(table);
    }

    // Switch expressions rather than nested ternaries: Sonar S3358 is a build error here.
    private static string SavedColor(long saved) => saved switch
    {
        > 0 => "green",
        < 0 => "red",
        _ => "grey"
    };

    private static string PctColor(double pct) => pct switch
    {
        >= 80 => "green",
        >= 40 => "yellow",
        _ => "red"
    };

    private static string ImpactBar(long saved, long maxSaved)
    {
        // Positive savings always get at least one block so small rows stay visible.
        var filled = saved > 0 && maxSaved > 0
            ? (int)Math.Clamp(Math.Round((double)saved / maxSaved * ImpactWidth), 1, ImpactWidth)
            : 0;
        return $"[green]{new string('█', filled)}[/][grey]{new string('░', ImpactWidth - filled)}[/]";
    }
}
```

- [ ] **Step 4: Run the tests, verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj --filter "FullyQualifiedName~GainDashboardRendererTests"`
Expected: PASS (7 tests). If the impact-bar assertion fails on rounding, check the math: 850/1250×10 = 6.8 → `Math.Round` → 7 filled.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Cli/Formatting/GainDashboardRenderer.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/GainDashboardRendererTests.cs
git commit -m "feat: add rtk-style gain dashboard renderer"
```

---

### Task 4: Wire `GainCommand` to the renderer

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` (replace the table block, lines ~77–121)
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs`

**Interfaces:**
- Consumes: `GainDashboardRenderer.Render(IAnsiConsole, GainSummary, string)` (Task 3).
- Produces: `dtk gain` human output is the dashboard; `--json` additionally contains `TotalExecutionTime`; CSV path unchanged.

- [ ] **Step 1: Update the tests to the new layout (failing first)**

In `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs`:

1. Replace `ExecuteAsync_WithData_DisplaysAllColumnHeaders` body assertions:

```csharp
        console.Output.Should().Contain("Command");
        console.Output.Should().Contain("Runs");
        console.Output.Should().Contain("Without Tool");
        console.Output.Should().Contain("Used by Tool");
        console.Output.Should().Contain("Saved");
        console.Output.Should().Contain("Avg%");
        console.Output.Should().Contain("Impact");
```

2. Replace `ExecuteAsync_WithData_DisplaysTotalRowWithCorrectValues` entirely with a recap test (kills the same mutations against the recap block instead of the removed TOTAL row):

```csharp
    [Fact]
    public async Task ExecuteAsync_WithData_DisplaysRecapBlock()
    {
        var successDetail = new CommandGainDetail(3, 1500, 300, 1200, 80.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(3, 1500, 300, 1200, 80.0, successDetail)
        };
        var summary = new GainSummary(3, 1500, 300, 1200, 80.0, details, TimeSpan.FromSeconds(2));
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("DTK Token Savings (Global Scope)");
        console.Output.Should().Contain("Total commands:    3");
        console.Output.Should().Contain("Without tool:      1.5K");
        console.Output.Should().Contain("Used by tool:      300");
        console.Output.Should().Contain("Tokens saved:      1.2K (80.0%)");
        console.Output.Should().Contain("Efficiency meter:");
    }
```

3. Add scope-variant tests:

```csharp
    [Fact]
    public async Task ExecuteAsync_ProjectFlag_DisplaysProjectScope()
    {
        var successDetail = new CommandGainDetail(1, 1000, 100, 900, 90.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(1, 1000, 100, 900, 90.0, successDetail)
        };
        var summary = new GainSummary(1, 1000, 100, 900, 90.0, details);
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings
        {
            Project = true
        }, CancellationToken.None);

        console.Output.Should().Contain("DTK Token Savings (Project Scope)");
    }

    [Fact]
    public async Task ExecuteAsync_NonDefaultDaysAndCommand_DisplaysFiltersInScope()
    {
        var successDetail = new CommandGainDetail(1, 1000, 100, 900, 90.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(1, 1000, 100, 900, 90.0, successDetail)
        };
        var summary = new GainSummary(1, 1000, 100, 900, 90.0, details);
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings
        {
            Days = 7,
            Command = "build"
        }, CancellationToken.None);

        console.Output.Should().Contain("last 7 days");
        console.Output.Should().Contain("command: build");
    }
```

4. Add the JSON exec-time assertion:

```csharp
    [Fact]
    public async Task ExecuteAsync_JsonMode_IncludesExecutionTime()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new GainCommandSettings
        {
            Json = true
        }, CancellationToken.None);

        writer.ToString().Should().Contain("TotalExecutionTime");
    }
```

- [ ] **Step 2: Run the Gain command tests, verify the new/changed ones fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj --filter "FullyQualifiedName~GainCommandTests"`
Expected: FAIL — `ExecuteAsync_WithData_DisplaysRecapBlock`, the scope tests, and `DisplaysAllColumnHeaders` (no "Avg%"/"Impact" yet). `ExecuteAsync_JsonMode_IncludesExecutionTime` already passes (Task 2 added the field) — that is expected.

- [ ] **Step 3: Rewire `GainCommand`**

In `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs`:

1. Add the using: `using DotnetTokenKiller.Cli.Formatting;`
2. Replace everything from `var table = new Table()` through `console.Write(table);` (the block after the no-data check) with:

```csharp
        GainDashboardRenderer.Render(console, summary, BuildScope(settings));
        return 0;
```

3. Add the scope builder as a private method (next to `EscapeCsv`):

```csharp
    private static string BuildScope(GainCommandSettings settings)
    {
        var scope = settings.Project ? "Project Scope" : "Global Scope";
        if (settings.Days != 30)
        {
            scope += string.Create(CultureInfo.InvariantCulture, $", last {settings.Days} days");
        }

        if (settings.Command is not null)
        {
            scope += $", command: {settings.Command}";
        }

        return scope;
    }
```

(The `30` matches the `GainCommandSettings.Days` default; the scope only mentions days when the user overrode it.)

- [ ] **Step 4: Run the Gain command tests, verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj --filter "FullyQualifiedName~GainCommandTests"`
Expected: PASS (all, including the untouched JSON/CSV tests).

- [ ] **Step 5: Full verification**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

Expected: build SUCCESS, all tests PASS, no formatting drift. If format reports drift, run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` and include the fixes.

Then eyeball the real output:

```bash
dotnet run --project src/DotnetTokenKiller.Cli -- gain
```

Expected: the dashboard renders with colors, K/M units, meter, and impact bars against your real tracking DB (or the "No data yet" message on a fresh machine).

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Cli/Commands/GainCommand.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs
git commit -m "feat: render dtk gain as rtk-style dashboard"
```
