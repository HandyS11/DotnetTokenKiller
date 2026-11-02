using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>Persists command output to a file and optionally returns a hint message.</summary>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="teeDirOverride">Optional directory override; uses platform default when null.</param>
public sealed class FileTeeService(IConfigProvider configProvider, string? teeDirOverride) : ITeeService
{
    /// <summary>How many bytes of a log's header pruning reads: the same as the log index does.</summary>
    private const int HeadBytes = FileTeeLogStore.HeadBytes;

    /// <summary>
    /// How many bytes of an undersized body pruning reads to find its last line: comfortably more
    /// than the longest truncation marker (about 50 bytes) plus the line feeds around it.
    /// </summary>
    private const int TailBytes = 256;

    /// <summary>Initializes a new instance using the default tee directory.</summary>
    /// <param name="configProvider">The configuration provider.</param>
    public FileTeeService(IConfigProvider configProvider)
        : this(configProvider, null)
    {
    }

    /// <inheritdoc/>
    public async Task<ITeeSession> BeginAsync(
        string commandSlug,
        TeeLogHeader provisional,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provisional);

        // Defensive: a caller-supplied ExitCode here is never correct (the run has not finished),
        // and one slipping through as non-null renders the header as already Complete. That leaves
        // FinalizeAsync's read-back guard below unable to find the running region it expects to
        // overwrite -- IndexOf returns -1, and AsSpan(0, -1) throws into the outer catch, silently
        // downgrading this run to a NullTeeSession. Normalizing here makes the API hard to misuse
        // instead of relying on every caller to remember the contract.
        provisional = provisional with { ExitCode = null };
        try
        {
            var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            var teeConfig = config.Tee;

            // Failures mode is deliberately not consulted here: the exit code does not exist yet.
            // It decides, in FinalizeAsync, which older logs of this kind the finished run supersedes.
            if (teeConfig.Mode == TeeMode.Never)
            {
                return NullTeeSession.Instance;
            }

            var teeDir = TeeDirectoryResolver.Resolve(teeConfig, teeDirOverride);
            Directory.CreateDirectory(teeDir);
            RestrictToOwnerOnly(teeDir);

            // Rotation does NOT run here: until the run finishes, this log is not yet the latest
            // complete run of its kind and has not yet superseded anything, so rotating now could
            // evict an older log for a run that is then killed. FileTeeSession runs it instead, in
            // FinalizeAsync, once the log is finalized and kept.
            var slug = TeeLogFileName.Sanitize(commandSlug);
            var fileName = TeeLogFileName.Build(
                provisional.TimestampUtc, Guid.NewGuid().ToString("N"), slug);
            var filePath = Path.Combine(teeDir, fileName);

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                // ReadWrite: FinalizeAsync reads the status/exit region back before overwriting it,
                // which throws NotSupportedException on a write-only stream. ReadWrite share mode
                // lets `dtk log` (and FileTeeLogStore) open the same file for reading while this
                // handle is still open for writing — required on Windows, where a second handle's
                // share mode must permit every access the first handle already holds.
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite
            };

            // Keep the file owner-only (0600) on POSIX; UnixCreateMode is unsupported on Windows.
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            // CA2000 cannot see that ownership of the stream transfers to the FileTeeSession
            // returned below; the catch block disposes it on every path that does not reach that
            // return.
#pragma warning disable CA2000
            var stream = new FileStream(filePath, options);
#pragma warning restore CA2000
            try
            {
                var rendered = provisional.Render();
                var region = TeeLogHeader.RenderStatusAndExit(null);
                var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
                var regionOffset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
                var regionLength = Encoding.UTF8.GetByteCount(region);

                await stream.WriteAsync(Encoding.UTF8.GetBytes(rendered), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                // BodyBytesWritten can never exceed MaxFileSizeBytes, so a hardcoded 500-byte guard
                // would silently discard every older log once the configured cap drops below it —
                // clamp the guard to whichever is smaller instead.
                return new FileTeeSession(stream, filePath, regionOffset, regionLength,
                    new TeeSessionPolicy(
                        MaxBodyBytes: teeConfig.MaxFileSizeBytes,
                        MinBodyBytes: Math.Min(500L, teeConfig.MaxFileSizeBytes),
                        KeepOnlyOnFailure: teeConfig.Mode == TeeMode.Failures,
                        TeeDir: teeDir,
                        MaxFiles: teeConfig.MaxFiles,
                        Slug: slug));
            }
            catch
            {
                // The FileTeeSession that would otherwise own the stream was never constructed, so
                // ownership never transferred to it; this method must dispose it before rethrowing
                // to the outer catch, which turns the failure into a NullTeeSession.
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
            return NullTeeSession.Instance;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            var teeDir = TeeDirectoryResolver.Resolve(config.Tee, teeDirOverride);
            if (!Directory.Exists(teeDir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(teeDir, "*.log"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: cleanup errors must never surface to the user (but cancellation must propagate)
        }
    }

    private static void RestrictToOwnerOnly(string teeDir)
    {
        // Tee logs can carry secrets from a failed command's output, so both the directory and the
        // file are kept owner-only (0700 / 0600). On Windows this is a no-op (POSIX modes only).
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                teeDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Deletes the oldest tee logs in excess of <paramref name="maxFiles"/>.</summary>
    /// <param name="teeDir">The tee directory to rotate.</param>
    /// <param name="maxFiles">
    /// Maximum number of tee files to retain; non-positive disables rotation entirely (deleting
    /// every file would not be a sane interpretation of "no limit").
    /// </param>
    /// <remarks>
    /// Called from <see cref="FileTeeSession.FinalizeAsync"/> after a log is confirmed kept — never
    /// from <see cref="BeginAsync"/> — so the log being rotated in is already on disk and counted
    /// among <paramref name="teeDir"/>'s files; no slot needs to be reserved for it.
    /// </remarks>
    internal static void RotateFiles(string teeDir, int maxFiles)
    {
        if (maxFiles <= 0)
        {
            return;
        }

        if (!Directory.Exists(teeDir))
        {
            return;
        }

        // Only rotate expected tee artifacts (log files) to avoid deleting unrelated files.
        var files = Directory.GetFiles(teeDir, "*.log").Order().ToList();
        var excess = files.Count - maxFiles;
        for (var i = 0; i < excess; i++)
        {
            File.Delete(files[i]);
        }
    }

    /// <summary>
    /// Deletes the logs a newly kept log supersedes: older logs of the same kind from the same
    /// project that are passing (when <paramref name="dropPassing"/>) or, finished and not truncated,
    /// smaller than <paramref name="minBodyBytes"/>.
    /// </summary>
    /// <param name="teeDir">The tee directory.</param>
    /// <param name="slug">The kept log's sanitised slug, as in its file name.</param>
    /// <param name="keptFilePath">The log just kept. It, and every log not older than it, is never touched.</param>
    /// <param name="minBodyBytes">Older logs whose bodies are smaller than this are dropped.</param>
    /// <param name="dropPassing">Whether older logs with exit code 0 are dropped whatever their size.</param>
    /// <remarks>
    /// <para>
    /// This is what lets every run's own log be kept without the directory filling with passing runs:
    /// in Failures mode each kind keeps one passing slot, the latest run's. Kinds are compared per
    /// project, because <c>dtk log</c> is scoped to the working directory: pruning another project's
    /// latest run would make <c>dtk log</c> there fall back to an older, stale log.
    /// </para>
    /// <para>
    /// Several dtk processes share the directory (an agent often builds and tests in parallel), so
    /// this deletes only what it can prove superseded: never a log whose header is still running
    /// (another process's open log, or one killed mid-flight that rotation will age out), never one
    /// whose name or header does not parse (including one whose header is not written yet), and never
    /// one that did not start strictly before the kept log. Ties keep both. Each deletion is
    /// best-effort: a log another process already deleted, or one still open without
    /// <see cref="FileShare.Delete"/> on Windows, is skipped.
    /// </para>
    /// </remarks>
    internal static void PruneSuperseded(
        string teeDir, string slug, string keptFilePath, long minBodyBytes, bool dropPassing)
    {
        if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(teeDir) ||
            !TeeLogFileName.TryParse(Path.GetFileName(keptFilePath), out var keptStart, out _) ||
            !TryReadLogFacts(keptFilePath, minBodyBytes: 0, out var kept))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(teeDir, "*" + TeeLogFileName.Extension))
        {
            try
            {
                if (IsSupersededBy(path, kept.Header.ProjectPath))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Intentional: a log deleted or rotated by another process since the listing, or
                // one this process cannot open or unlink, is simply left for the next run.
            }
        }

        bool IsSupersededBy(string path, string projectPath)
        {
            if (!TeeLogFileName.TryParse(Path.GetFileName(path), out var start, out var candidateSlug) ||
                start >= keptStart ||
                !string.Equals(candidateSlug, slug, StringComparison.OrdinalIgnoreCase) ||
                !TryReadLogFacts(path, minBodyBytes, out var facts) ||
                facts.Header.Status != TeeLogStatus.Complete ||
                !string.Equals(facts.Header.ProjectPath, projectPath, StringComparison.Ordinal))
            {
                return false;
            }

            return (dropPassing && facts.Header.ExitCode == 0) || facts.IsUndersized;
        }
    }

    /// <summary>
    /// Reads a log's header and whether its body is under the size floor without having been
    /// truncated, reading only the head of the file and, for a body under the floor, its tail.
    /// </summary>
    /// <param name="path">The log's path.</param>
    /// <param name="minBodyBytes">The size floor.</param>
    /// <param name="facts">The header and the size verdict, when the header parsed.</param>
    /// <returns><see langword="false"/> when the file holds no complete, parseable header.</returns>
    private static bool TryReadLogFacts(string path, long minBodyBytes, out LogFacts facts)
    {
        facts = default;

        // ReadWrite: another process may still hold its log open for writing (required on Windows,
        // see BeginAsync). Delete: this brief read must not stop another process's prune or rotation.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var buffer = new byte[(int)Math.Min(HeadBytes, length)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var head = Encoding.UTF8.GetString(buffer, 0, read);
        if (!TeeLogHeader.TryParse(head, out var header))
        {
            return false;
        }

        // The header itself decoded intact (TryParse needed its delimiter), so its UTF-8 length is
        // exact even when the buffer cut a multi-byte rune in the body after it.
        var headerText = head.AsSpan(0, head.Length - TeeLogHeader.StripHeader(head).Length);
        var headerBytes = Encoding.UTF8.GetByteCount(headerText);
        var bodyBytes = length - headerBytes;
        var undersized = bodyBytes < minBodyBytes && !EndsWithTruncationMarker(stream, headerBytes, bodyBytes);
        facts = new LogFacts(header, undersized);
        return true;
    }

    /// <summary>Whether a body's last line is the truncation marker, read from the body's tail.</summary>
    /// <param name="stream">The open log.</param>
    /// <param name="bodyOffset">The body's byte offset.</param>
    /// <param name="bodyBytes">The body's byte length.</param>
    /// <returns><see langword="true"/> when the log was truncated at its byte cap.</returns>
    private static bool EndsWithTruncationMarker(FileStream stream, long bodyOffset, long bodyBytes)
    {
        // The marker is well under TailBytes long, so a tail this size always holds the line feed
        // before it; a rune cut at the tail's start garbles only an earlier line.
        var tailBytes = (int)Math.Min(bodyBytes, TailBytes);
        if (tailBytes <= 0)
        {
            return false;
        }

        var buffer = new byte[tailBytes];
        stream.Seek(bodyOffset + bodyBytes - tailBytes, SeekOrigin.Begin);
        var read = stream.ReadAtLeast(buffer, tailBytes, throwOnEndOfStream: false);
        var tail = Encoding.UTF8.GetString(buffer, 0, read);

        // The same last line TeeLogRenderer inspects: one trailing line feed is not a line of its own.
        var trimmed = tail.EndsWith('\n') ? tail[..^1] : tail;
        return TeeTruncationMarker.IsMarkerLine(trimmed[(trimmed.LastIndexOf('\n') + 1)..].TrimEnd('\r'));
    }

    /// <summary>What pruning needs to know about one log.</summary>
    /// <param name="Header">The parsed header.</param>
    /// <param name="IsUndersized">Whether the body is under the size floor and was not truncated.</param>
    private readonly record struct LogFacts(TeeLogHeader Header, bool IsUndersized);
}
