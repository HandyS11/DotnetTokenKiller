using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class FileTeeServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-test-{Guid.NewGuid()}");

    private FileTeeService CreateSut(TeeConfig? teeConfig = null)
    {
        var config = DtkConfig.Default with
        {
            Tee = teeConfig ?? new TeeConfig()
        };
        return new FileTeeService(new FakeConfigProvider(config), _tempDir);
    }

    private static string LargeOutput(int length = 600)
    {
        return new string('x', length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TeeAndHintAsync_WritesFileAndReturnsHint_WhenFailuresMode_NonZeroExit()
    {
        var sut = CreateSut(new TeeConfig());

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 1);

        hint.Should().StartWith("[full output: ").And.EndWith(".log]");
        Directory.GetFiles(_tempDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task TeeAndHintAsync_ReturnsNull_WhenFailuresMode_ZeroExit()
    {
        var sut = CreateSut(new TeeConfig());

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        hint.Should().BeNull();
        Directory.Exists(_tempDir).Should().BeFalse();
    }

    [Fact]
    public async Task TeeAndHintAsync_ReturnsNull_WhenNeverMode()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Never));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 1);

        hint.Should().BeNull();
    }

    [Fact]
    public async Task TeeAndHintAsync_WritesFile_WhenAlwaysMode_ZeroExit()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        hint.Should().NotBeNull();
        Directory.GetFiles(_tempDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task TeeAndHintAsync_ReturnsNull_WhenOutputTooSmall()
    {
        var sut = CreateSut(new TeeConfig());

        var hint = await sut.TeeAndHintAsync(new string('x', 499), "build", 1);

        hint.Should().BeNull();
    }

    [Fact]
    public async Task TeeAndHintAsync_DeletesOldestFiles_WhenMaxFilesExceeded()
    {
        // Pre-populate tee dir with maxFiles existing files
        Directory.CreateDirectory(_tempDir);
        for (var i = 0; i < 3; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"{i:D10}_old.log"), "old");
        }

        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFiles: 3));

        await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        Directory.GetFiles(_tempDir).Should().HaveCount(3); // stays at maxFiles
    }

    [Fact]
    public async Task TeeAndHintAsync_SanitizesSlug_InFileName()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await sut.TeeAndHintAsync(LargeOutput(), "dotnet::run --project", 0);

        var file = Directory.GetFiles(_tempDir).Single();
        Path.GetFileName(file).Should().Contain("_dotnet-run-project.log");
    }

    [Fact]
    public async Task TeeAndHintAsync_TruncatesContent_WhenOutputExceedsMaxSize()
    {
        const long maxBytes = 100L;
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFileSizeBytes: maxBytes));

        await sut.TeeAndHintAsync(LargeOutput(600), "build", 0);

        var file = Directory.GetFiles(_tempDir).Single();
        var content = await File.ReadAllTextAsync(file);
        content.Length.Should().Be(100);
    }

    /// <summary>Nested fake — avoids NSubstitute dependency (not referenced in this test csproj).</summary>
    /// <param name="config">The configuration to return from <see cref="LoadAsync"/>.</param>
    private sealed class FakeConfigProvider(DtkConfig config) : IConfigProvider
    {
        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(config);
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
