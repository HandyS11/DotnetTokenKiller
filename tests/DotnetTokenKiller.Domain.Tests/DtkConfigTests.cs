using DotnetTokenKiller.Domain.Configuration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class DtkConfigTests
{
    [Fact]
    public void DtkConfig_Default_has_expected_tracking_values()
    {
        var config = DtkConfig.Default;

        config.Tracking.Enabled.Should().BeTrue();
        config.Tracking.RetentionDays.Should().Be(90);
        config.Tracking.DbPath.Should().BeNull();
        config.Tracking.Tokenizer.Should().Be(TokenizerModel.Cl100kBase);
    }

    [Fact]
    public void DtkConfig_Default_has_expected_display_values()
    {
        var config = DtkConfig.Default;

        config.Display.Colors.Should().BeTrue();
        config.Display.Emoji.Should().BeTrue();
        config.Display.Width.Should().Be(120);
    }

    [Fact]
    public void DtkConfig_Default_has_expected_tee_values()
    {
        var config = DtkConfig.Default;

        config.Tee.Mode.Should().Be(TeeMode.Failures);
        config.Tee.Directory.Should().BeNull();
        config.Tee.MaxFiles.Should().Be(20);
        config.Tee.MaxFileSizeBytes.Should().Be(1_048_576L);
    }

    [Fact]
    public void DtkConfig_Default_each_call_returns_new_instance()
    {
        var a = DtkConfig.Default;
        var b = DtkConfig.Default;

        // Records are value-equal but different instances
        a.Should().Be(b);
    }

    [Fact]
    public void TrackingConfig_defaults_are_correct()
    {
        var config = new TrackingConfig();

        config.Enabled.Should().BeTrue();
        config.RetentionDays.Should().Be(90);
        config.DbPath.Should().BeNull();
        config.Tokenizer.Should().Be(TokenizerModel.Cl100kBase);
    }

    [Fact]
    public void DisplayConfig_defaults_are_correct()
    {
        var config = new DisplayConfig();

        config.Colors.Should().BeTrue();
        config.Emoji.Should().BeTrue();
        config.Width.Should().Be(120);
    }

    [Fact]
    public void TeeConfig_defaults_are_correct()
    {
        var config = new TeeConfig();

        config.Mode.Should().Be(TeeMode.Failures);
        config.Directory.Should().BeNull();
        config.MaxFiles.Should().Be(20);
        config.MaxFileSizeBytes.Should().Be(1_048_576L);
    }

    [Fact]
    public void DtkConfig_supports_with_expression_for_immutable_updates()
    {
        var original = DtkConfig.Default;
        var modified = original with
        {
            Tracking = original.Tracking with
            {
                Enabled = false
            }
        };

        modified.Tracking.Enabled.Should().BeFalse();
        original.Tracking.Enabled.Should().BeTrue();
    }
}
