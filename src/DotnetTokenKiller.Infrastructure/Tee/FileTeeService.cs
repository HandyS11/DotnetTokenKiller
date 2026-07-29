using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>Persists command output to a file and optionally returns a hint message.</summary>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="teeDirOverride">Optional directory override; uses platform default when null.</param>
public sealed class FileTeeService(IConfigProvider configProvider, string? teeDirOverride) : ITeeService
{
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
            // The log is written provisionally and discarded in FinalizeAsync if the run succeeded.
            if (teeConfig.Mode == TeeMode.Never)
            {
                return NullTeeSession.Instance;
            }

            var teeDir = TeeDirectoryResolver.Resolve(teeConfig, teeDirOverride);
            Directory.CreateDirectory(teeDir);
            RestrictToOwnerOnly(teeDir);

            // Rotation does NOT run here: at this point it is unknown whether this run's own log
            // will be kept (Failures mode defers that decision to the exit code, and the 500-byte
            // guard defers it to the body size). FileTeeSession runs it instead, in FinalizeAsync,
            // once the log is confirmed kept.
            var fileName = TeeLogFileName.Build(
                provisional.TimestampUtc, Guid.NewGuid().ToString("N"), commandSlug);
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
                // would silently discard every log once the configured cap drops below it — clamp
                // the guard to whichever is smaller instead.
                return new FileTeeSession(stream, filePath, regionOffset, regionLength,
                    teeConfig.MaxFileSizeBytes, minBodyBytes: Math.Min(500L, teeConfig.MaxFileSizeBytes),
                    teeConfig.Mode == TeeMode.Failures, teeDir, teeConfig.MaxFiles);
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
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            teeDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
}
