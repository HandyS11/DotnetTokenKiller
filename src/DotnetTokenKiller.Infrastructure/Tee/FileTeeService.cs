using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>Persists command output to a file and optionally returns a hint message.</summary>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="teeDirOverride">Optional directory override; uses platform default when null.</param>
public sealed partial class FileTeeService(IConfigProvider configProvider, string? teeDirOverride) : ITeeService
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
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawOutput);
        try
        {
            var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            var teeConfig = config.Tee;

            var shouldWrite = teeConfig.Mode switch
            {
                TeeMode.Always => true,
                TeeMode.Failures => exitCode != 0,
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

            var teeDir = GetTeeDir(teeConfig, teeDirOverride);
            Directory.CreateDirectory(teeDir);
            RestrictToOwnerOnly(teeDir);

            // Rotate: delete oldest files if at/over limit
            RotateFiles(teeDir, teeConfig.MaxFiles);

            // Truncate to the configured byte budget without splitting a multi-byte UTF-8 sequence.
            var content = TruncateToUtf8Bytes(rawOutput, teeConfig.MaxFileSizeBytes);

            // Write file
            var slug = SanitizeSlug(commandSlug);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var uniqueSuffix = Guid.NewGuid().ToString("N");
            var fileName = $"{timestamp}_{uniqueSuffix}_{slug}.log";
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
    public async Task DeleteLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            var teeDir = GetTeeDir(config.Tee, teeDirOverride);
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

    private static string TruncateToUtf8Bytes(string text, long maxBytes)
    {
        // MaxFileSizeBytes is a byte budget; slicing the string by char count could overshoot the cap
        // (multi-byte runes) or split a rune and emit U+FFFD. Cut on a UTF-8 code-point boundary instead.
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        // Walk runes and stop before the budget is exceeded rather than materializing the whole
        // string as a byte[] — captured output can be very large, and that allocation is the OOM
        // risk TeeAndHintAsync swallows (silently dropping the log). Slicing on a rune boundary also
        // guarantees we never split a multi-byte sequence.
        var chars = 0;
        var runeBytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (runeBytes + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            runeBytes += rune.Utf8SequenceLength;
            chars += rune.Utf16SequenceLength;
        }

        return text[..chars];
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

    private static string GetTeeDir(TeeConfig config, string? teeDirOverride)
    {
        if (teeDirOverride is not null)
        {
            return teeDirOverride;
        }

        var envVar = EnvironmentOverride.Read("DTK_TEE_DIR");
        if (envVar is not null)
        {
            return envVar;
        }

        if (!string.IsNullOrEmpty(config.Directory))
        {
            return config.Directory;
        }

        return GetDefaultTeeDir();
    }

    /// <summary>Returns the default tee output directory used when no override is configured.</summary>
    /// <returns>The platform-default tee directory.</returns>
    public static string GetDefaultTeeDir()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "dtk", "tee");
    }

    private static string SanitizeSlug(string slug)
    {
        var safe = NonSafeCharRegex().Replace(slug, "-");
        safe = CollapseHyphensRegex().Replace(safe, "-");
        return safe.Trim('-');
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    private static partial Regex NonSafeCharRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex CollapseHyphensRegex();
}
