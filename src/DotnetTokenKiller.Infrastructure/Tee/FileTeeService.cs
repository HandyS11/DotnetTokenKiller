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
    public async Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        TeeLogHeader header,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawOutput);
        ArgumentNullException.ThrowIfNull(header);
        try
        {
            var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            var teeConfig = config.Tee;

            var shouldWrite = teeConfig.Mode switch
            {
                TeeMode.Always => true,
                TeeMode.Failures => header.ExitCode is not 0,
                _ => false
            };

            if (!shouldWrite)
            {
                return null;
            }

            // Size guard
            if (rawOutput.Length < 500)
            {
                return null;
            }

            var teeDir = TeeDirectoryResolver.Resolve(teeConfig, teeDirOverride);
            Directory.CreateDirectory(teeDir);
            RestrictToOwnerOnly(teeDir);

            // Rotate: delete oldest files if at/over limit
            RotateFiles(teeDir, teeConfig.MaxFiles);

            // The budget applies to the body alone — the header is dtk's own addition, and charging
            // the user's configured cap for it would silently shrink every existing setting.
            var body = Utf8Text.TruncateToUtf8Bytes(rawOutput, teeConfig.MaxFileSizeBytes);
            var content = header.Render() + body;

            var fileName = TeeLogFileName.Build(
                header.TimestampUtc, Guid.NewGuid().ToString("N"), commandSlug);
            var filePath = Path.Combine(teeDir, fileName);
            await WriteOwnerOnlyAsync(filePath, content, cancellationToken).ConfigureAwait(false);

            return $"[full output: {filePath}]";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<ITeeSession> BeginAsync(
        string commandSlug,
        TeeLogHeader provisional,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provisional);
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
            RotateFiles(teeDir, teeConfig.MaxFiles);

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

                return new FileTeeSession(stream, filePath, regionOffset, regionLength,
                    teeConfig.MaxFileSizeBytes, minBodyBytes: 500, teeConfig.Mode == TeeMode.Failures);
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

    private static async Task WriteOwnerOnlyAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        // Keep the file owner-only (0600) on POSIX; UnixCreateMode is unsupported on Windows.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        // Write UTF-8 bytes directly (no BOM), matching the repo's no-BOM policy.
        var bytes = Encoding.UTF8.GetBytes(content);
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var stream = new FileStream(filePath, options);
#pragma warning restore CA2007
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void RotateFiles(string teeDir, int maxFiles)
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
        var excess = files.Count - maxFiles + 1; // +1 to make room for new file
        for (var i = 0; i < excess; i++)
        {
            File.Delete(files[i]);
        }
    }
}
