using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>Tracking write cost: a single <c>INSERT</c> against an already-populated table.</summary>
/// <remarks>
/// Deliberately carries no <c>RowCount</c> axis. BenchmarkDotNet calls a <c>[Benchmark]</c> method
/// many times — pilot, warmup and measured iterations can add up to hundreds or thousands of calls
/// for an operation this fast — and <see cref="RecordAsync"/> inserts a new row on every call with
/// no reset in between, so the table grows past its seeded size well before the run ends. A
/// <c>RowCount</c> parameter here would label that growing, uncontrolled size as if it were fixed,
/// which is exactly the false label the original combined benchmark carried. <c>[IterationSetup]</c>
/// is not the fix: BenchmarkDotNet's own guidance is that per-iteration setup distorts measurements
/// in the sub-100-microsecond range this benchmark sits in, so the cure would be worse than the
/// disease. Running with a growing table instead of a reset one is acceptable specifically because
/// an <c>INSERT</c>'s cost is essentially independent of how many rows already exist — unlike the
/// read side, which is why that one still carries the axis this one omits.
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet owns this type's lifecycle through [GlobalSetup]/[GlobalCleanup] "
                    + "and never calls Dispose() on a benchmark instance itself. CleanupAsync already "
                    + "disposes _tracker there via DisposeAsync, which a synchronous IDisposable on "
                    + "this class could not express any better.")]
public class TrackerWriteBenchmarks
{
    /// <summary>
    /// A modest non-empty baseline so the benchmark measures an insert into a populated table
    /// rather than an empty one, without the run time seeding <see cref="TrackerReadBenchmarks"/>'s
    /// 10,000-row tier would add.
    /// </summary>
    private const int SeedRowCount = 1_000;

    private HermeticState? _state;
    private SqliteTracker? _tracker;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _state = HermeticState.Enter();
        _tracker = new SqliteTracker($"Data Source={_state.DbPath}");

        for (var i = 0; i < SeedRowCount; i++)
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
    public async Task RecordAsync() =>
        await _tracker!.RecordAsync(CommandRecordFactory.NewRecord(0)).ConfigureAwait(false);
}
