using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure.Configuration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Configuration;

public sealed class JsonConfigProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-test-{Guid.NewGuid()}");

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private JsonConfigProvider CreateSut()
    {
        return new JsonConfigProvider(ConfigPath);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenNoFileExists()
    {
        var sut = CreateSut();

        var config = sut.Load();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public void Load_ReturnsMergedConfig_WhenFileExists()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """{"Tracking":{"RetentionDays":45}}""");
        var sut = CreateSut();

        var config = sut.Load();

        config.Tracking.RetentionDays.Should().Be(45);
        config.Tracking.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenJsonIsInvalid()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, "not valid json {{ }}");
        var sut = CreateSut();

        var config = sut.Load();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenJsonIsNullLiteral()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, "null");
        var sut = CreateSut();

        var config = sut.Load();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenNoFileExists()
    {
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenJsonIsInvalid()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, "not valid json {{ }}");
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public async Task LoadAsync_MergesPartialOverrides_WithDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, """{"Tracking":{"RetentionDays":45}}""");
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Tracking.RetentionDays.Should().Be(45);
        config.Tracking.Enabled.Should().BeTrue();
        config.Display.Should().Be(DtkConfig.Default.Display);
        config.Tee.Should().Be(DtkConfig.Default.Tee);
    }

    [Fact]
    public async Task LoadAsync_MergesPartialOverrides_WhenSubRecordHasMissingValueTypeFields()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, """{"Tee":{"MaxFiles":5}}""");
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Tee.MaxFiles.Should().Be(5);
        config.Tee.Mode.Should().Be(TeeMode.Failures);
        config.Tee.MaxFileSizeBytes.Should().Be(1_048_576L);
        config.Tracking.Should().Be(DtkConfig.Default.Tracking);
        config.Display.Should().Be(DtkConfig.Default.Display);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var sut = CreateSut();
        var modified = DtkConfig.Default with
        {
            Tracking = new TrackingConfig(false, 30)
        };

        await sut.SaveAsync(modified);
        var loaded = await sut.LoadAsync();

        loaded.Tracking.Enabled.Should().BeFalse();
        loaded.Tracking.RetentionDays.Should().Be(30);
        loaded.Tracking.Tokenizer.Should().Be(TokenizerModel.Cl100kBase);
        loaded.Display.Should().Be(DtkConfig.Default.Display);
        loaded.Tee.Should().Be(DtkConfig.Default.Tee);
    }

    [Fact]
    public async Task LoadAsync_MergesTokenizerOverride_WithDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, """{ "Tracking": { "Tokenizer": "O200kBase" } }""");
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Tracking.Tokenizer.Should().Be(TokenizerModel.O200kBase);
        config.Tracking.Enabled.Should().BeTrue();
        config.Tracking.RetentionDays.Should().Be(90);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsTokenizer()
    {
        var sut = CreateSut();
        var modified = DtkConfig.Default with
        {
            Tracking = new TrackingConfig(Tokenizer: TokenizerModel.O200kBase)
        };

        await sut.SaveAsync(modified);
        var loaded = await sut.LoadAsync();

        loaded.Tracking.Tokenizer.Should().Be(TokenizerModel.O200kBase);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory_WhenNotExisting()
    {
        var sut = CreateSut();

        await sut.SaveAsync(DtkConfig.Default);

        File.Exists(ConfigPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenJsonIsNullLiteral()
    {
        // Covers loaded is null branch (line 25): JSON "null" deserializes to null
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, "null");
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public async Task SaveAsync_FilenameOnlyPath_FallsBackToCurrentDirectory()
    {
        // Covers Path.GetDirectoryName returning "" → directory = CurrentDirectory (lines 37-40)
        const string filename = "dtk-test-config-fallback.json";
        var expectedPath = Path.Combine(Environment.CurrentDirectory, filename);
        try
        {
            var sut = new JsonConfigProvider(filename);
            await sut.SaveAsync(DtkConfig.Default);
            File.Exists(expectedPath).Should().BeTrue();
        }
        finally
        {
            File.Delete(expectedPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_DeletesFile_WhenFileExists()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, "{}");
        var sut = CreateSut();

        await sut.DeleteAsync();

        File.Exists(ConfigPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_DoesNotThrow_WhenFileDoesNotExist()
    {
        var sut = CreateSut();
        File.Exists(ConfigPath).Should().BeFalse();

        var act = () => sut.DeleteAsync();

        await act.Should().NotThrowAsync();
    }
}
