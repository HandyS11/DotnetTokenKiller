using DotnetTokenKiller.Domain.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests.Tee;

public sealed class TeeLogFileNameTests
{
    [Theory]
    [InlineData("build", "build")]
    [InlineData("list package", "list-package")]
    [InlineData("weird/../name", "weird-name")]
    [InlineData("--leading-and-trailing--", "leading-and-trailing")]
    public void Sanitize_ProducesAFilesystemSafeSlug(string input, string expected)
    {
        TeeLogFileName.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_ReplacesUnderscore_SoTheSlugCannotAddAField()
    {
        // The filename is underscore-delimited, so an underscore inside the slug would make
        // TryParse see four fields and reject its own output.
        TeeLogFileName.Sanitize("a_b").Should().Be("a-b");
    }

    [Fact]
    public void Build_ThenTryParse_RoundTripsTimestampAndSlug()
    {
        var timestamp = new DateTimeOffset(2026, 7, 28, 9, 14, 2, TimeSpan.Zero);

        var fileName = TeeLogFileName.Build(timestamp, "abc123", "list package");

        TeeLogFileName.TryParse(fileName, out var parsedTime, out var slug).Should().BeTrue();
        parsedTime.Should().Be(timestamp);
        slug.Should().Be("list-package");
    }

    [Fact]
    public void Build_ProducesTheHistoricalShape()
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1785264642362);

        var fileName = TeeLogFileName.Build(timestamp, "abc123", "build");

        // Same shape as files written by earlier versions, so ordinal sorting of a mixed
        // directory still orders old and new files together.
        fileName.Should().Be("1785264642362_abc123_build.log");
    }

    [Theory]
    [InlineData("not-a-log-file.txt")]
    [InlineData("missing-fields.log")]
    [InlineData("notanumber_abc123_build.log")]
    [InlineData("1785264642362_abc123_build_extra.log")]
    public void TryParse_ReturnsFalse_ForNamesItDidNotProduce(string fileName)
    {
        TeeLogFileName.TryParse(fileName, out _, out _).Should().BeFalse();
    }
}
