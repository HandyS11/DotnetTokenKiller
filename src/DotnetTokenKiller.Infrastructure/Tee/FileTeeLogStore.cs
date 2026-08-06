using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>Reads tee logs from the configured tee directory.</summary>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="teeDirOverride">Optional directory override; uses the resolved default when null.</param>
public sealed class FileTeeLogStore(IConfigProvider configProvider, string? teeDirOverride) : ITeeLogStore
{
    /// <summary>
    /// How many bytes of each file to read when building the index. Large enough to contain a
    /// complete header including a long path and command line; a header cut short by this limit
    /// fails to parse and the log lists as project-unknown rather than being misread.
    /// </summary>
    private const int HeadBytes = 4096;

    /// <summary>Initializes a new instance using the resolved tee directory.</summary>
    /// <param name="configProvider">The configuration provider.</param>
    public FileTeeLogStore(IConfigProvider configProvider)
        : this(configProvider, null)
    {
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TeeLogEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var teeDir = TeeDirectoryResolver.Resolve(config.Tee, teeDirOverride);
        if (!Directory.Exists(teeDir))
        {
            return [];
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(teeDir, "*" + TeeLogFileName.Extension);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // The directory exists but cannot be enumerated — e.g. permission denied on the 0700
            // tee directory. A diagnostic command should report "no logs found" here, the same as
            // for an absent directory, rather than surface a stack trace.
            return [];
        }

        var entries = new List<TeeLogEntry>();
        foreach (var path in files)
        {
            var entry = await TryReadEntryAsync(path, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return [.. entries.OrderByDescending(e => e.TimestampUtc)];
    }

    /// <inheritdoc/>
    public async Task<string> ReadBodyAsync(TeeLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // File.ReadAllTextAsync cannot set a share mode; a session's writer stream is still open
        // for the file's whole lifetime, and on Windows a second handle can only be opened if it
        // permits the write access the first handle already holds, hence FileShare.ReadWrite here.
        string text;
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using (var stream = new FileStream(
            entry.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
#pragma warning restore CA2007
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return TeeLogHeader.StripHeader(text);
    }

    private static async Task<TeeLogEntry?> TryReadEntryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            var fileName = Path.GetFileName(path);

            // A name this type did not produce still lists — falling back to the write time is
            // better than hiding a file the user can see in the directory.
            var timestamp = TeeLogFileName.TryParse(fileName, out var fromName, out var parsedSlug)
                ? fromName
                : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var slug = parsedSlug;

            var head = await ReadHeadAsync(path, cancellationToken).ConfigureAwait(false);
            var header = TeeLogHeader.TryParse(head, out var parsed) ? parsed : null;

            return new TeeLogEntry(path, header, info.Length, timestamp, slug);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log deleted by rotation between enumeration and reading (IOException), or one whose
            // permissions this user cannot satisfy (UnauthorizedAccessException), is not an error
            // worth failing the whole listing over — drop the entry and list the rest.
            return null;
        }
    }

    private static async Task<string> ReadHeadAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[HeadBytes];
        // FileShare.ReadWrite: a session's writer stream may still be open for this file (see the
        // comment in ReadBodyAsync).
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
#pragma warning restore CA2007
        // A single ReadAsync call is not guaranteed to fill the buffer even when enough data is
        // available, and a file shorter than HeadBytes must not be treated as a short-read error —
        // ReadAtLeastAsync with throwOnEndOfStream: false handles both: it keeps reading until the
        // buffer is full or the stream ends, returning however many bytes were actually available.
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
