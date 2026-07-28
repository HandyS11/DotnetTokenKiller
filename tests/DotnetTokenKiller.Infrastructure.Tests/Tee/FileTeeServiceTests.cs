using System.Reflection;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

/// <summary>
/// Shares the "TeeDirectoryResolver" collection with <see cref="TeeDirectoryResolverTests"/>: both
/// mutate the process-wide DTK_TEE_DIR environment variable, and xUnit only serialises test classes
/// against each other when they are in the same collection — distinct collections still run in
/// parallel by default, so this is what actually prevents the two classes racing on that variable.
/// </summary>
[Collection("TeeDirectoryResolver")]
public sealed class FileTeeServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

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

    [Fact]
    public async Task TeeAndHintAsync_WritesFileAndReturnsHint_WhenFailuresMode_NonZeroExit()
    {
        var sut = CreateSut(new TeeConfig());

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 1);

        hint.Should().StartWith("[full output: ").And.EndWith(".log]");
        Directory.GetFiles(_tempDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task TeeAndHintAsync_HintContainsOpenableFullPath()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        hint.Should().NotBeNull();
        var path = ExtractPathFromHint(hint!);
        Path.IsPathRooted(path).Should().BeTrue(); // old hint: bare filename, not rooted
        File.Exists(path).Should().BeTrue();        // old hint: File.Exists false from any other cwd
    }

    private static string ExtractPathFromHint(string hint)
    {
        const string prefix = "[full output: ";
        return hint[prefix.Length..^1]; // strip prefix and the trailing ']'
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

        await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        var file = Directory.GetFiles(_tempDir).Single();
        new FileInfo(file).Length.Should().BeLessThanOrEqualTo(maxBytes);
        // ASCII input: the byte cap equals the char count exactly.
        var content = await File.ReadAllTextAsync(file);
        content.Length.Should().Be(100);
    }

    [Fact]
    public async Task TeeAndHintAsync_TruncationDoesNotSplitMultiByteChar_AndRespectsByteCap()
    {
        // '🚀' (U+1F680) is 4 UTF-8 bytes; a byte cap that lands mid-rune must drop the whole rune,
        // never emit a replacement char, and never exceed the cap.
        const long maxBytes = 102L; // 25 rockets = 100 bytes, so the cap falls inside the 26th
        var rockets = string.Concat(Enumerable.Repeat("🚀", 600));
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFileSizeBytes: maxBytes));

        await sut.TeeAndHintAsync(rockets, "build", 0);

        var file = Directory.GetFiles(_tempDir).Single();
        new FileInfo(file).Length.Should().BeLessThanOrEqualTo(maxBytes);
        var content = await File.ReadAllTextAsync(file);
        content.Should().NotContain("�"); // no split-rune replacement character
        content.Should().Be(string.Concat(Enumerable.Repeat("🚀", 25)));
    }

    [Fact]
    public async Task TeeAndHintAsync_WritesLogFileOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits are not meaningful on Windows
        }

        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        var file = Directory.GetFiles(_tempDir).Single();
        File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task TeeAndHintAsync_CreatesTeeDirectoryOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits are not meaningful on Windows
        }

        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await sut.TeeAndHintAsync(LargeOutput(), "build", 0);

        File.GetUnixFileMode(_tempDir)
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
        // Covers the tee directory resolution using the DTK_TEE_DIR environment variable when
        // teeDirOverride is null.
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
        // Covers the tee directory resolution using config.Directory when
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

        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-nonexistent-{Guid.NewGuid()}");
        Directory.Exists(nonExistentDir).Should().BeFalse();

        var act = () => method.Invoke(null, [nonExistentDir, 5]);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task DeleteLogsAsync_DeletesLogFiles_LeavesNonLogFiles()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.log"), "log1");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.log"), "log2");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "keep");
        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await sut.DeleteLogsAsync();

        Directory.GetFiles(_tempDir, "*.log").Should().BeEmpty();
        File.Exists(Path.Combine(_tempDir, "notes.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteLogsAsync_NonExistentDirectory_DoesNotThrow()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        Directory.Exists(_tempDir).Should().BeFalse();

        var act = () => sut.DeleteLogsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteLogsAsync_ConfigProviderThrows_DoesNotThrow()
    {
        // Covers catch block in DeleteLogsAsync (lines 94-97): exceptions must never surface
        var sut = new FileTeeService(new ThrowingConfigProvider(), _tempDir);

        var act = () => sut.DeleteLogsAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>Nested fake — avoids NSubstitute dependency (not referenced in this test csproj).</summary>
    /// <param name="initialConfig">The configuration to return from <see cref="LoadAsync"/>.</param>
    private sealed class FakeConfigProvider(DtkConfig initialConfig) : IConfigProvider
    {
        public DtkConfig Load()
        {
            return initialConfig;
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(initialConfig);
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingConfigProvider : IConfigProvider
    {
        public DtkConfig Load()
        {
            throw new InvalidOperationException("Simulated config failure");
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated config failure");
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
