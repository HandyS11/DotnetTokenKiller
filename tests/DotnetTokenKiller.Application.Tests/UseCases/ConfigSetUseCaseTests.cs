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
            .AndDoes(call => _savedConfig = call.Arg<DtkConfig>()!);
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
    public async Task ExecuteAsync_DisplayEmoji_False_SavesUpdatedValue()
    {
        await _sut.ExecuteAsync("display.emoji", "false");

        _savedConfig.Display.Emoji.Should().BeFalse();
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
        await _sut.ExecuteAsync("DISPLAY.EMOJI", "false");

        _savedConfig.Display.Emoji.Should().BeFalse();
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

    [Theory]
    [InlineData("tracking.enabled")]
    [InlineData("tracking.retentionDays")]
    [InlineData("tracking.dbPath")]
    [InlineData("tracking.tokenizer")]
    [InlineData("display.emoji")]
    [InlineData("tee.mode")]
    [InlineData("tee.directory")]
    [InlineData("tee.maxFiles")]
    [InlineData("tee.maxFileSizeBytes")]
    public void SupportedKeys_ContainsAllExpectedKeys(string key)
    {
        ConfigSetUseCase.SupportedKeys.Should().ContainKey(key);
    }

    [Fact]
    public async Task ExecuteAsync_TrackingEnabled_True_SavesTrue()
    {
        // Kills boolean mutation on ParseBool return (line 128)
        await _sut.ExecuteAsync("tracking.enabled", "true");

        _savedConfig.Tracking.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RetentionDaysAtMin_Succeeds()
    {
        // Kills equality mutation: result <= min instead of result < min (line 152)
        await _sut.ExecuteAsync("tracking.retentionDays", "1");

        _savedConfig.Tracking.RetentionDays.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_MaxFileSizeBytesAtMin_Succeeds()
    {
        // Kills equality mutation: result <= min instead of result < min (line 169)
        await _sut.ExecuteAsync("tee.maxFileSizeBytes", "0");

        _savedConfig.Tee.MaxFileSizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_TeeMaxFilesAtMin_Succeeds()
    {
        // Kills equality mutation on min bound (ParseInt)
        await _sut.ExecuteAsync("tee.maxFiles", "1");

        _savedConfig.Tee.MaxFiles.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_NullableString_NonEmpty_ReturnsValue()
    {
        // Kills boolean mutation on NullableString (line 180): string.IsNullOrEmpty check
        await _sut.ExecuteAsync("tracking.dbPath", "/my/db.sqlite");

        _savedConfig.Tracking.DbPath.Should().Be("/my/db.sqlite");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownKey_ErrorMessageContainsKey()
    {
        // Kills string mutation on unknown key error message (line 185)
        var act = () => _sut.ExecuteAsync("bad.key", "value");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*bad.key*");
    }

    [Fact]
    public async Task ExecuteAsync_NullKey_ThrowsArgumentNullException()
    {
        // Kills statement mutation on ThrowIfNull(key) — line 39
        var act = () => _sut.ExecuteAsync(null!, "value");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullValue_ThrowsArgumentNullException()
    {
        // Kills statement mutation on ThrowIfNull(value) — line 40
        var act = () => _sut.ExecuteAsync("tracking.enabled", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownKey_ErrorListsSupportedKeys()
    {
        // Kills string mutation on "Supported keys:" in error message
        var act = () => _sut.ExecuteAsync("x.y", "z");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*tracking.enabled*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidBool_ErrorIncludesValueAndKey()
    {
        // Kills string mutations on ParseBool error format
        var act = () => _sut.ExecuteAsync("tracking.enabled", "nope");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*nope*tracking.enabled*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidEnum_ErrorListsValidValues()
    {
        // Kills string mutations on ParseEnum error format
        var act = () => _sut.ExecuteAsync("tracking.tokenizer", "bad");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*bad*tracking.tokenizer*Cl100kBase*");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyEnumValue_ThrowsFormatException()
    {
        // Empty value for an enum key must fail with a friendly FormatException, not an
        // IndexOutOfRangeException from indexing value[0].
        var act = () => _sut.ExecuteAsync("tracking.tokenizer", "");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Expected one of*");
    }

    [Fact]
    public async Task ExecuteAsync_NumericEnumInput_ThrowsFormatException()
    {
        // Enum.TryParse accepts numeric strings like "2" and produces undefined enum values — must be rejected
        var act = () => _sut.ExecuteAsync("tracking.tokenizer", "2");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Expected one of*");
    }

    [Fact]
    public async Task ExecuteAsync_NumericEnumInput_TeeMode_ThrowsFormatException()
    {
        var act = () => _sut.ExecuteAsync("tee.mode", "99");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Expected one of*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidInt_ErrorIncludesValueAndKey()
    {
        // Kills string mutations on ParseInt error format
        var act = () => _sut.ExecuteAsync("tracking.retentionDays", "abc");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*abc*tracking.retentionDays*");
    }

    [Fact]
    public async Task ExecuteAsync_IntBelowMin_ErrorIncludesMinimum()
    {
        // Kills string/arithmetic mutations on "Minimum allowed value is {min}" format
        var act = () => _sut.ExecuteAsync("tee.maxFiles", "0");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Minimum allowed value is 1*");
    }

    [Fact]
    public async Task ExecuteAsync_LongBelowMin_ErrorIncludesMinimum()
    {
        // Kills string/arithmetic mutations on ParseLong "Minimum allowed value is {min}" format
        var act = () => _sut.ExecuteAsync("tee.maxFileSizeBytes", "-5");

        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Minimum allowed value is 0*");
    }
}
