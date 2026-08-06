using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tracking;

/// <summary>
/// The DI registration and the non-DI passthrough entry point both build their tracker here, so
/// what this resolves is what every run writes to.
/// </summary>
public sealed class TrackerFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-trackerfactory-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static CommandRecord Record() => new(
        DateTimeOffset.UtcNow,
        "build",
        "/proj",
        new TokenStatistics(1000, 150, 850, 85.0),
        TimeSpan.FromMilliseconds(500));

    private static DtkConfig ConfigWithDbPath(string? dbPath) =>
        DtkConfig.Default with { Tracking = new TrackingConfig(DbPath: dbPath) };

    [Fact]
    public async Task Create_WritesToTheConfiguredDbPath()
    {
        var configured = Path.Combine(_tempDir, "configured", "tracking.db");
        var saved = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        Environment.SetEnvironmentVariable("DTK_DB_PATH", null);
        try
        {
            await using var tracker = TrackerFactory.Create(ConfigWithDbPath(configured));
            await tracker.RecordAsync(Record());

            File.Exists(configured).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_DB_PATH", saved);
        }
    }

    [Fact]
    public async Task Create_PrefersTheEnvironmentOverride_OverTheConfiguredDbPath()
    {
        // The override is what every integration test (and any user debugging a run) relies on to
        // keep a run away from the real database, so it has to win over the config file.
        var configured = Path.Combine(_tempDir, "configured", "tracking.db");
        var overridden = Path.Combine(_tempDir, "override", "tracking.db");
        var saved = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        Environment.SetEnvironmentVariable("DTK_DB_PATH", overridden);
        try
        {
            await using var tracker = TrackerFactory.Create(ConfigWithDbPath(configured));
            await tracker.RecordAsync(Record());

            File.Exists(overridden).Should().BeTrue();
            File.Exists(configured).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_DB_PATH", saved);
        }
    }

    [Fact]
    public void Create_FallsBackToThePlatformDefault_WhenNeitherOverrideNorConfigSetsAPath()
    {
        // Constructed but never written to: the fallback target is the developer's own
        // %LocalAppData%/dtk/tracking.db, and a test must not create or touch that.
        var saved = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        Environment.SetEnvironmentVariable("DTK_DB_PATH", null);
        try
        {
            var act = () => TrackerFactory.Create(ConfigWithDbPath(null)).Dispose();

            act.Should().NotThrow();
            SqliteTracker.GetDefaultDbPath().Should().EndWith(Path.Combine("dtk", "tracking.db"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_DB_PATH", saved);
        }
    }

    [Fact]
    public void Create_NullConfig_Throws()
    {
        var act = () => TrackerFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
