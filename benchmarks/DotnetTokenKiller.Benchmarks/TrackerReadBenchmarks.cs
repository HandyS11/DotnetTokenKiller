using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Aggregation cost as history accumulates. The <c>RowCount</c> parameter is the point of this
/// benchmark: <c>dtk gain</c> reads every retained record, so its cost grows with how long the user
/// has had dtk installed — a regression nobody would ever reproduce on a fresh database.
/// </summary>
/// <remarks>
/// Split out from the write benchmark (see <see cref="TrackerWriteBenchmarks"/>) because
/// <c>GetSummaryAsync</c> is a pure read: nothing in the benchmark method mutates the table, so the
/// <c>RowCount</c> label stays exact for every iteration BenchmarkDotNet runs, including pilot and
/// warmup ones.
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet owns this type's lifecycle through [GlobalSetup]/[GlobalCleanup] "
                    + "and never calls Dispose() on a benchmark instance itself. CleanupAsync already "
                    + "disposes _tracker there via DisposeAsync, which a synchronous IDisposable on "
                    + "this class could not express any better.")]
public class TrackerReadBenchmarks
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
            await _tracker.RecordAsync(CommandRecordFactory.NewRecord(i)).ConfigureAwait(false);
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
    public async Task<GainSummary> GetSummaryAsync() =>
        await _tracker!.GetSummaryAsync(days: 30, projectPath: null).ConfigureAwait(false);
}
