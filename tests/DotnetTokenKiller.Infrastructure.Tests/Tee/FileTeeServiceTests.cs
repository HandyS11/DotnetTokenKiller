using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using System.Reflection;
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

    [Fact]
    public async Task TeeAndHintAsync_ConfigProviderThrows_ReturnsNull()
    {
        // Covers catch-all block (lines 70-73): exceptions inside the try never surface
        var sut = new FileTeeService(new ThrowingConfigProvider(), _tempDir);

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 1);

        hint.Should().BeNull();
    }

    [Fact]
    public async Task TeeAndHintAsync_MaxFilesZero_DoesNotRotate()
    {
        // Covers RotateFiles early return when maxFiles <= 0 (lines 80-81)
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFiles: 0));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        hint.Should().NotBeNull();
        Directory.GetFiles(_tempDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task TeeAndHintAsync_DtkTeeDirEnvVar_UsesEnvVarDirectory()
    {
        // Covers GetTeeDir returning the env-var path (lines 107-108)
        var envDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-env-{Guid.NewGuid()}");
        var originalValue = Environment.GetEnvironmentVariable("DTK_TEE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("DTK_TEE_DIR", envDir);
            var config = DtkConfig.Default with
            {
                Tee = new TeeConfig(TeeMode.Always)
            };
            // Use the single-param constructor so teeDirOverride is null → falls through to env var
            var sut = new FileTeeService(new FakeConfigProvider(config));

            var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

            hint.Should().NotBeNull();
            Directory.Exists(envDir).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_TEE_DIR", originalValue);
            if (Directory.Exists(envDir))
            {
                Directory.Delete(envDir, true);
            }
        }
    }

    [Fact]
    public async Task TeeAndHintAsync_ConfigDirectorySet_UsesConfigDirectory()
    {
        // Covers GetTeeDir returning config.Directory (lines 112-113) when
        // teeDirOverride is null and DTK_TEE_DIR is not set
        var configDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-configdir-{Guid.NewGuid()}");
        var originalEnv = Environment.GetEnvironmentVariable("DTK_TEE_DIR");
        Environment.SetEnvironmentVariable("DTK_TEE_DIR", null);
        try
        {
            var config = DtkConfig.Default with
            {
                Tee = new TeeConfig(TeeMode.Always, configDir)
            };
            var sut = new FileTeeService(new FakeConfigProvider(config));

            var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

            hint.Should().NotBeNull();
            Directory.Exists(configDir).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_TEE_DIR", originalEnv);
            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, true);
            }
        }
    }

    [Fact]
    public void RotateFiles_NonExistentDirectory_ReturnsEarlyWithoutThrowing()
    {
        // Covers RotateFiles early return when dir does not exist (lines 85-86) via reflection
        var method = typeof(FileTeeService)
            .GetMethod("RotateFiles", BindingFlags.NonPublic | BindingFlags.Static)!;

        var act = () => method.Invoke(null, ["/nonexistent/path/xyz/tee", 5]);

        act.Should().NotThrow();
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

    private sealed class ThrowingConfigProvider : IConfigProvider
    {
        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated config failure");
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
