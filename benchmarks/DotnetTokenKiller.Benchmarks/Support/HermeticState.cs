namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// Redirects dtk's three on-disk state locations into a temporary directory for the life of a
/// benchmark, and deletes them afterwards.
/// </summary>
/// <remarks>
/// Without this, running the benchmarks would write thousands of synthetic records into the
/// developer's real tracking database and skew their own <c>dtk gain</c> report — and the benchmark
/// numbers would themselves depend on how much history happened to be there.
/// </remarks>
internal sealed class HermeticState : IDisposable
{
    private readonly DirectoryInfo _root;
    private readonly string? _previousConfigPath;
    private readonly string? _previousDbPath;
    private readonly string? _previousTeeDir;

    private HermeticState()
    {
        _root = Directory.CreateTempSubdirectory("dtk-bench-");
        _previousConfigPath = Environment.GetEnvironmentVariable("DTK_CONFIG_PATH");
        _previousDbPath = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        _previousTeeDir = Environment.GetEnvironmentVariable("DTK_TEE_DIR");

        ConfigPath = Path.Combine(_root.FullName, "config.json");
        DbPath = Path.Combine(_root.FullName, "tracking.db");
        TeeDir = Path.Combine(_root.FullName, "logs");
        Directory.CreateDirectory(TeeDir);

        Environment.SetEnvironmentVariable("DTK_CONFIG_PATH", ConfigPath);
        Environment.SetEnvironmentVariable("DTK_DB_PATH", DbPath);
        Environment.SetEnvironmentVariable("DTK_TEE_DIR", TeeDir);
    }

    /// <summary>The temporary directory everything else lives under, deleted on dispose.</summary>
    internal string RootPath => _root.FullName;

    internal string ConfigPath { get; }

    internal string DbPath { get; }

    internal string TeeDir { get; }

    internal static HermeticState Enter() => new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DTK_CONFIG_PATH", _previousConfigPath);
        Environment.SetEnvironmentVariable("DTK_DB_PATH", _previousDbPath);
        Environment.SetEnvironmentVariable("DTK_TEE_DIR", _previousTeeDir);

        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a benchmark run over.
        }
    }
}
