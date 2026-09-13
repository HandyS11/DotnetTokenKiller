using System.Diagnostics;
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
/// mid-write can only leave a truncated file of its own, which a fold deletes. A write lands under
/// a temporary name and is renamed to its final name only once it is complete, so a fold never
/// opens, moves, or reads a file that a writer still has open.
/// </remarks>
/// <param name="root">The journal directory.</param>
/// <param name="lockWait">How long a waiting fold retries for the lock; 2 s unless a test shortens it.</param>
internal sealed class PendingRecordJournal(string root, TimeSpan? lockWait = null)
{
    private const string FileExtension = ".json";
    private const string TempExtension = ".tmp";
    private const string LockFileName = ".lock";
    private const string ClaimPrefix = "folding-";
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);
    private readonly TimeSpan _lockWait = lockWait ?? TimeSpan.FromSeconds(2);

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
        var finalPath = Path.Combine(Root, name);
        var tempPath = finalPath + TempExtension;

        await WriteTempFileAsync(tempPath, record, cancellationToken).ConfigureAwait(false);

        // Renamed only once the file is closed and complete, so a fold can never see a half-written
        // file under its final ".json" name.
        File.Move(tempPath, finalPath);
    }

    private static async Task WriteTempFileAsync(string tempPath, CommandRecord record, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var stream = new FileStream(tempPath, options);
#pragma warning restore CA2007
        await JsonSerializer.SerializeAsync(
                stream, PendingRecord.From(record), PendingRecordJsonContext.Default.PendingRecord, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The number of runs waiting to be folded; zero when the directory does not exist.</summary>
    public int Count() =>
        Directory.Exists(Root) ? Directory.EnumerateFiles(Root, "*" + FileExtension).Count() : 0;

    /// <summary>
    /// Folds every waiting run into the database exactly once, whatever process dies when.
    /// </summary>
    /// <remarks>
    /// Under an exclusive lock: stale (over an hour old) <c>*.tmp</c> files left by a writer killed
    /// between create and rename are deleted first; leftover claim directories are then deleted if
    /// <paramref name="committed"/> knows their id (a fold that died after its commit) or refolded
    /// if not (one that died before); then every pending file is moved into a new claim directory,
    /// parsed, and handed to <paramref name="commit"/> together with every claim id involved, in one
    /// call, ordered by file name across every claimed directory together (file names start with
    /// UTC ticks, so this keeps run order). The claim directories are deleted only after the commit
    /// returns. A commit that throws leaves them for the next fold; a process that dies releases the
    /// lock with its handle.
    /// </remarks>
    /// <param name="committed">Whether the database already holds the fold with this id.</param>
    /// <param name="commit">Inserts the records and every fold id in one transaction.</param>
    /// <param name="wait">
    /// Retry for the lock for up to the journal's wait and then throw <see cref="TimeoutException"/>
    /// (readers), or return at once when it is busy (a writer's background fold).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FoldOutcome> FoldAsync(
        Func<string, CancellationToken, Task<bool>> committed,
        Func<IReadOnlyList<string>, IReadOnlyList<CommandRecord>, CancellationToken, Task> commit,
        bool wait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(commit);

        Directory.CreateDirectory(Root);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var lockHandle = await TryLockAsync(wait, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
        if (lockHandle is null)
        {
            return new FoldOutcome(false, 0, 0);
        }

        DeleteStaleTempFiles();

        var claimIds = new List<string>();
        var claimDirs = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(Root, ClaimPrefix + "*").ToList())
        {
            var id = Path.GetFileName(dir)[ClaimPrefix.Length..];
            if (await committed(id, cancellationToken).ConfigureAwait(false))
            {
                Directory.Delete(dir, recursive: true);
            }
            else
            {
                claimIds.Add(id);
                claimDirs.Add(dir);
            }
        }

        var files = Directory.EnumerateFiles(Root, "*" + FileExtension).ToList();
        if (files.Count > 0)
        {
            var id = Guid.NewGuid().ToString("N");
            var dir = Path.Combine(Root, ClaimPrefix + id);
            Directory.CreateDirectory(dir);
            foreach (var file in files)
            {
                File.Move(file, Path.Combine(dir, Path.GetFileName(file)));
            }

            claimIds.Add(id);
            claimDirs.Add(dir);
        }

        if (claimDirs.Count == 0)
        {
            return new FoldOutcome(true, 0, 0);
        }

        var records = new List<CommandRecord>();
        var corrupt = 0;
        var claimedFiles = claimDirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*" + FileExtension))
            .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal)
            .ToList();
        foreach (var file in claimedFiles)
        {
            var pending = await TryReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (pending is null)
            {
                corrupt++;
                File.Delete(file);
                continue;
            }

            try
            {
                records.Add(pending.ToCommandRecord());
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                // A record that parses as JSON but whose fields do not convert, such as an empty
                // timestamp or command, is just as corrupt as one that fails to parse. Otherwise one
                // bad file would fail every fold of its claim forever.
                corrupt++;
                File.Delete(file);
            }
        }

        await commit(claimIds, records, cancellationToken).ConfigureAwait(false);

        foreach (var dir in claimDirs)
        {
            Directory.Delete(dir, recursive: true);
        }

        return new FoldOutcome(true, records.Count, corrupt);
    }

    /// <summary>Deletes every pending file and claim directory. The lock file stays; it is never deleted.</summary>
    /// <remarks>
    /// Does not take the journal lock: a fold running concurrently in another process may fail, or
    /// insert records it had already claimed. Acceptable because a reset is user-initiated and rare.
    /// </remarks>
    public void Clear()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*" + FileExtension).ToList())
        {
            File.Delete(file);
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*" + TempExtension).ToList())
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.EnumerateDirectories(Root, ClaimPrefix + "*").ToList())
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Deletes <c>*.tmp</c> files older than <see cref="StaleTempAge"/>: a writer killed between create and rename.</summary>
    private void DeleteStaleTempFiles()
    {
        var staleBefore = DateTime.UtcNow - StaleTempAge;
        foreach (var file in Directory.EnumerateFiles(Root, "*" + TempExtension)
                     .Where(file => File.GetLastWriteTimeUtc(file) < staleBefore)
                     .ToList())
        {
            File.Delete(file);
        }
    }

    private static async Task<PendingRecord?> TryReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var stream = File.OpenRead(file);
#pragma warning restore CA2007
            return await JsonSerializer
                .DeserializeAsync(stream, PendingRecordJsonContext.Default.PendingRecord, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A process killed mid-write leaves a truncated file; nothing else writes here.
            return null;
        }
    }

    /// <summary>
    /// Opens the lock file with <see cref="FileShare.None"/>: an exclusive <c>flock</c> on Unix, a
    /// sharing violation for everyone else on Windows, released with the handle in both cases.
    /// </summary>
    /// <param name="wait">Retry until the journal's wait elapses, or return <see langword="null"/> at once when busy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TimeoutException">The lock was still held after the journal's wait.</exception>
    private async Task<FileStream?> TryLockAsync(bool wait, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Root, LockFileName);
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            try
            {
                return new FileStream(path, options);
            }
            catch (IOException) when (!wait)
            {
                return null;
            }
            catch (IOException ex) when (Stopwatch.GetElapsedTime(started) >= _lockWait)
            {
                throw new TimeoutException($"Another process held the tracking journal lock at {path} for over {_lockWait.TotalSeconds:0.#} s.", ex);
            }
            catch (IOException)
            {
                await Task.Delay(LockRetry, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
