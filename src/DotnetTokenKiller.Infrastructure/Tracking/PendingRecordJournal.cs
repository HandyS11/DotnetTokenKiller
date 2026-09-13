using System.Globalization;
using System.Text.Json;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>
/// Where a tracked run leaves its record without opening SQLite: one JSON file per run under
/// <c>pending</c> beside the database, folded into the database by whoever reads it next.
/// </summary>
/// <remarks>
/// One file per run rather than one appended file: no two processes ever hold the same handle, so
/// neither POSIX append atomicity nor Windows share modes are relied on, and a process killed
/// mid-write can only leave a truncated file of its own, which a fold deletes.
/// </remarks>
/// <param name="root">The journal directory.</param>
internal sealed class PendingRecordJournal(string root)
{
    private const string FileExtension = ".json";

    /// <summary>The journal directory, created on the first write.</summary>
    public string Root { get; } = root;

    /// <summary>Writes one file for <paramref name="record"/>. Costs a directory check and one small file; no fsync.</summary>
    /// <param name="record">The run to journal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteAsync(CommandRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(Root);

        // Ticks first so a directory listing is chronological; the pid and a random suffix keep two
        // processes, or two runs in one tick, from colliding. CreateNew turns a collision into an
        // exception rather than a silently shared file.
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow.Ticks:D19}-{Environment.ProcessId}-{Guid.NewGuid():N}{FileExtension}");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var stream = new FileStream(Path.Combine(Root, name), options);
#pragma warning restore CA2007
        await JsonSerializer.SerializeAsync(
                stream, PendingRecord.From(record), PendingRecordJsonContext.Default.PendingRecord, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The number of runs waiting to be folded; zero when the directory does not exist.</summary>
    public int Count() =>
        Directory.Exists(Root) ? Directory.EnumerateFiles(Root, "*" + FileExtension).Count() : 0;
}
