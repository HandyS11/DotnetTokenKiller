using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class ConfigSetUseCaseTests
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly ConfigSetUseCase _sut;
    private DtkConfig _savedConfig = DtkConfig.Default;

    public ConfigSetUseCaseTests()
    {
        _sut = new ConfigSetUseCase(_configProvider);
        _configProvider.LoadAsync().ReturnsForAnyArgs(DtkConfig.Default);
        _configProvider.SaveAsync(null!)
            .ReturnsForAnyArgs(Task.CompletedTask)
            .AndDoes(call => _savedConfig = call.Arg<DtkConfig>());
    }

    [Fact]
    public async Task ExecuteAsync_TrackingEnabled_False_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tracking.enabled", "false");

        _savedConfig.Tracking.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_TrackingRetentionDays_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tracking.retentionDays", "60");

        _savedConfig.Tracking.RetentionDays.Should().Be(60);
    }

    [Fact]
    public async Task ExecuteAsync_TrackingDbPath_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tracking.dbPath", "/custom/path.db");

        _savedConfig.Tracking.DbPath.Should().Be("/custom/path.db");
    }

    [Fact]
    public async Task ExecuteAsync_TrackingDbPath_EmptyString_ResetsToNull()
    {
        await _sut.ExecuteAsync("tracking.dbPath", "");

        _savedConfig.Tracking.DbPath.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_TrackingTokenizer_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tracking.tokenizer", "O200kBase");

        _savedConfig.Tracking.Tokenizer.Should().Be(TokenizerModel.O200kBase);
    }

    [Fact]
    public async Task ExecuteAsync_DisplayColors_False_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("display.colors", "false");

        _savedConfig.Display.Colors.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DisplayEmoji_False_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("display.emoji", "false");

        _savedConfig.Display.Emoji.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DisplayWidth_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("display.width", "80");

        _savedConfig.Display.Width.Should().Be(80);
    }

    [Fact]
    public async Task ExecuteAsync_TeeMode_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tee.mode", "Always");

        _savedConfig.Tee.Mode.Should().Be(TeeMode.Always);
    }

    [Fact]
    public async Task ExecuteAsync_TeeDirectory_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tee.directory", "/logs/tee");

        _savedConfig.Tee.Directory.Should().Be("/logs/tee");
    }

    [Fact]
    public async Task ExecuteAsync_TeeDirectory_EmptyString_ResetsToNull()
    {
        await _sut.ExecuteAsync("tee.directory", "");

        _savedConfig.Tee.Directory.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_TeeMaxFiles_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tee.maxFiles", "10");

        _savedConfig.Tee.MaxFiles.Should().Be(10);
    }

    [Fact]
    public async Task ExecuteAsync_TeeMaxFileSizeBytes_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("tee.maxFileSizeBytes", "2097152");

        _savedConfig.Tee.MaxFileSizeBytes.Should().Be(2_097_152L);
    }

    [Fact]
    public async Task ExecuteAsync_KeyIsCaseInsensitive_Succeeds()
    {
        await _sut.ExecuteAsync("DISPLAY.WIDTH", "100");

        _savedConfig.Display.Width.Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownKey_ThrowsArgumentException()
    {
        var act = () => _sut.ExecuteAsync("unknown.key", "value");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown configuration key*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidBoolValue_ThrowsFormatException()
    {
        var act = () => _sut.ExecuteAsync("tracking.enabled", "yes");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*true or false*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidIntValue_ThrowsFormatException()
    {
        var act = () => _sut.ExecuteAsync("tracking.retentionDays", "notanumber");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*integer*");
    }

    [Fact]
    public async Task ExecuteAsync_RetentionDaysBelowMin_ThrowsFormatException()
    {
        var act = () => _sut.ExecuteAsync("tracking.retentionDays", "0");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Minimum*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidLongValue_ThrowsFormatException()
    {
        // Covers ParseLong parse failure (lines 164-166) — tee.maxFileSizeBytes uses ParseLong
        var act = () => _sut.ExecuteAsync("tee.maxFileSizeBytes", "notanumber");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*integer*");
    }

    [Fact]
    public async Task ExecuteAsync_LongValueBelowMin_ThrowsFormatException()
    {
        // Covers ParseLong below-min check (lines 170-172)
        var act = () => _sut.ExecuteAsync("tee.maxFileSizeBytes", "-1");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Minimum*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidEnumValue_ThrowsFormatException()
    {
        var act = () => _sut.ExecuteAsync("tee.mode", "invalid");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Expected one of*");
    }

    [Fact]
    public async Task ExecuteAsync_DisplayWidthBelowMin_ThrowsFormatException()
    {
        var act = () => _sut.ExecuteAsync("display.width", "10");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Minimum*");
    }

    [Theory]
    [InlineData("tracking.enabled")]
    [InlineData("tracking.retentionDays")]
    [InlineData("tracking.dbPath")]
    [InlineData("tracking.tokenizer")]
    [InlineData("display.colors")]
    [InlineData("display.emoji")]
    [InlineData("display.width")]
    [InlineData("tee.mode")]
    [InlineData("tee.directory")]
    [InlineData("tee.maxFiles")]
    [InlineData("tee.maxFileSizeBytes")]
    public void SupportedKeys_ContainsAllExpectedKeys(string key)
    {
        ConfigSetUseCase.SupportedKeys.Should().ContainKey(key);
    }
}
