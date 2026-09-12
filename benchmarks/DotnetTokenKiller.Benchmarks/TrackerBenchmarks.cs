using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Tracking write cost, and aggregation cost as history accumulates. The <c>RowCount</c> parameter
/// is the point of the second benchmark: <c>dtk gain</c> reads every retained record, so its cost
/// grows with how long the user has had dtk installed — a regression nobody would ever reproduce on
/// a fresh database.
/// </summary>
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet owns this type's lifecycle through [GlobalSetup]/[GlobalCleanup]. "
                    + "CleanupAsync already disposes _tracker there via DisposeAsync, which a "
                    + "synchronous IDisposable on this class could not express any better.")]
public class TrackerBenchmarks
{
    private HermeticState? _state;
    private SqliteTracker? _tracker;

    [Params(100, 10_000)]
    public int RowCount { get; set; } = 100;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _state = HermeticState.Enter();
        _tracker = new SqliteTracker($"Data Source={_state.DbPath}");

        for (var i = 0; i < RowCount; i++)
        {
            await _tracker.RecordAsync(NewRecord(i)).ConfigureAwait(false);
        }
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_tracker is not null)
        {
            await _tracker.DisposeAsync().ConfigureAwait(false);
        }

        _state?.Dispose();
    }

    [Benchmark]
    public async Task RecordAsync() => await _tracker!.RecordAsync(NewRecord(0)).ConfigureAwait(false);

    [Benchmark]
    public async Task<GainSummary> GetSummaryAsync() =>
        await _tracker!.GetSummaryAsync(days: 30, projectPath: null).ConfigureAwait(false);

    private static CommandRecord NewRecord(int index) => new(
        DateTimeOffset.UtcNow.AddMinutes(-index),
        "build",
        "/repo",
        new TokenStatistics(1000, 100, 900, 90.0),
        TimeSpan.FromMilliseconds(1200));
}
