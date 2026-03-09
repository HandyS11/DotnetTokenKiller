using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class AnsiStripTests
{
    [Fact]
    public void Strip_PlainText_ReturnsUnchanged()
    {
        AnsiStrip.Strip("hello world").Should().Be("hello world");
    }

    [Fact]
    public void Strip_AnsiColorCode_RemovesCode()
    {
        AnsiStrip.Strip("\x1b[32mGREEN\x1b[0m").Should().Be("GREEN");
    }

    [Fact]
    public void Strip_MultipleSequences_RemovesAll()
    {
        AnsiStrip.Strip("\x1b[1m\x1b[31mERROR\x1b[0m: bad thing").Should().Be("ERROR: bad thing");
    }

    [Fact]
    public void Strip_EmptyString_ReturnsEmpty()
    {
        AnsiStrip.Strip(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void Strip_NullInput_ReturnsNull()
    {
        AnsiStrip.Strip(null!).Should().BeNull();
    }
}
