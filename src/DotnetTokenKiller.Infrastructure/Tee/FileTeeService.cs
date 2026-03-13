using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Infrastructure.Tee;

public sealed partial class FileTeeService : ITeeService
{
    private const string FailuresMode = "failures";
    private const string AlwaysMode = "always";

    private readonly IConfigProvider _configProvider;
    private readonly string? _teeDirOverride;

    public FileTeeService(IConfigProvider configProvider)
        : this(configProvider, null)
    {
    }

    /// <summary>Constructor for testability — pass temp dir to avoid touching real FS.</summary>
    /// <param name="configProvider">The configuration provider.</param>
    /// <param name="teeDirOverride">Optional directory override; uses platform default when null.</param>
    public FileTeeService(IConfigProvider configProvider, string? teeDirOverride)
    {
        _configProvider = configProvider;
        _teeDirOverride = teeDirOverride;
    }

    public async Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configProvider.LoadAsync(cancellationToken);
            var teeConfig = config.Tee;

            // Mode check
            var mode = teeConfig.Mode;
            var shouldWrite = mode.Equals(AlwaysMode, StringComparison.OrdinalIgnoreCase)
                              || (mode.Equals(FailuresMode, StringComparison.OrdinalIgnoreCase) && exitCode != 0);

            if (!shouldWrite)
            {
                return null;
            }

            // Size guard
            if (rawOutput.Length < 500)
            {
                return null;
            }

            var teeDir = GetTeeDir(teeConfig, _teeDirOverride);
            Directory.CreateDirectory(teeDir);

            // Rotate: delete oldest files if at/over limit
            RotateFiles(teeDir, teeConfig.MaxFiles);

            // Truncate content
            var maxChars = (int)Math.Min(teeConfig.MaxFileSizeBytes, int.MaxValue);
            var content = rawOutput.Length > maxChars ? rawOutput[..maxChars] : rawOutput;

            // Write file
            var slug = SanitizeSlug(commandSlug);
            var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{slug}.log";
            var filePath = Path.Combine(teeDir, fileName);
            await File.WriteAllTextAsync(filePath, content, cancellationToken);

            return $"[full output: {filePath}]";
        }
        catch
        {
            // Intentional: tee errors must never surface to the user
            return null;
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

        var envVar = Environment.GetEnvironmentVariable("DTK_TEE_DIR");
        if (!string.IsNullOrEmpty(envVar))
        {
            return envVar;
        }

        if (!string.IsNullOrEmpty(config.Directory))
        {
            return config.Directory;
        }

        return GetDefaultTeeDir();
    }

    private static string GetDefaultTeeDir()
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

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex CollapseHyphensRegex();
}
