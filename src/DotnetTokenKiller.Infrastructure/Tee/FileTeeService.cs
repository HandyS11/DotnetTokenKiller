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

            // Rotate: delete oldest files if at/over limit
            RotateFiles(teeDir, teeConfig.MaxFiles);

            // Truncate content
            var maxChars = Math.Max(0, (int)Math.Min(teeConfig.MaxFileSizeBytes, int.MaxValue));
            var content = rawOutput.Length > maxChars ? rawOutput[..maxChars] : rawOutput;

            // Write file
            var slug = SanitizeSlug(commandSlug);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var uniqueSuffix = Guid.NewGuid().ToString("N");
            var fileName = $"{timestamp}_{uniqueSuffix}_{slug}.log";
            var filePath = Path.Combine(teeDir, fileName);
            await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);

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
