using System.Text;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class Utf8TextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void TruncateToUtf8Bytes_ReturnsEmpty_WhenTheBudgetIsNotPositive(long maxBytes)
    {
        // A non-positive budget means there is no room at all; the rune walk below would otherwise
        // have to decide that on its own for every call.
        Utf8Text.TruncateToUtf8Bytes("anything", maxBytes).Should().BeEmpty();
    }

    [Fact]
    public void TruncateToUtf8Bytes_ReturnsTheTextUnchanged_WhenItFitsTheBudget()
    {
        Utf8Text.TruncateToUtf8Bytes("hello", 5).Should().Be("hello");
        Utf8Text.TruncateToUtf8Bytes("hello", 4096).Should().Be("hello");
    }

    [Fact]
    public void TruncateToUtf8Bytes_CountsBytesNotChars()
    {
        // "é" is two UTF-8 bytes, so a five-char budget is not a five-byte budget: slicing by char
        // count here would overshoot the cap the tee enforces.
        Utf8Text.TruncateToUtf8Bytes("ééé", 5).Should().Be("éé");
    }

    [Fact]
    public void TruncateToUtf8Bytes_CutsOnARuneBoundary_RatherThanSplittingACharacter()
    {
        // An odd budget cannot land evenly between two-byte characters. Cutting mid-sequence would
        // decode back as U+FFFD, which is what the tee's rune-boundary cut exists to prevent.
        var result = Utf8Text.TruncateToUtf8Bytes("ééé", 3);

        result.Should().Be("é");
        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(3);
        result.Should().NotContain("�");
    }

    [Fact]
    public void TruncateToUtf8Bytes_KeepsSurrogatePairsIntact()
    {
        // An emoji is one rune spanning two UTF-16 chars and four UTF-8 bytes; a budget that fits
        // only part of it must drop the whole thing rather than emit half a surrogate pair.
        const string emoji = "\U0001F600\U0001F600";

        Utf8Text.TruncateToUtf8Bytes(emoji, 7).Should().Be("\U0001F600");
        Utf8Text.TruncateToUtf8Bytes(emoji, 3).Should().BeEmpty();
    }

    [Fact]
    public void TruncateToUtf8Bytes_ReturnsEmpty_ForEmptyInput()
    {
        Utf8Text.TruncateToUtf8Bytes(string.Empty, 10).Should().BeEmpty();
    }
}
