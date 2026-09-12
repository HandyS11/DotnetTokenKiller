using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// Builds synthetic <see cref="CommandRecord"/> instances for seeding and exercising a tracker in
/// benchmarks, so the read and write tracker benchmarks build identical rows without duplicating
/// the same record shape between them.
/// </summary>
internal static class CommandRecordFactory
{
    /// <summary>Builds one synthetic "build" record, timestamped <paramref name="index"/> minutes in the past.</summary>
    /// <param name="index">
    /// Minutes to place the record in the past, and a convenient loop counter when seeding many rows.
    /// </param>
    internal static CommandRecord NewRecord(int index) => new(
        DateTimeOffset.UtcNow.AddMinutes(-index),
        "build",
        "/repo",
        new TokenStatistics(1000, 100, 900, 90.0),
        TimeSpan.FromMilliseconds(1200));
}
