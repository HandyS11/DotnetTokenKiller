using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class AnsiStripTests
{
    private const string Esc = "\e";
    private const string Bel = "\a";

    [Fact]
    public void Strip_PlainText_ReturnsUnchanged()
    {
        AnsiStrip.Strip("hello world").Should().Be("hello world");
    }

    [Fact]
    public void Strip_AnsiColorCode_RemovesCode()
    {
        AnsiStrip.Strip(Esc + "[32mGREEN" + Esc + "[0m").Should().Be("GREEN");
    }

    [Fact]
    public void Strip_MultipleSequences_RemovesAll()
    {
        AnsiStrip.Strip(Esc + "[1m" + Esc + "[31mERROR" + Esc + "[0m: bad thing").Should().Be("ERROR: bad thing");
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

    [Fact]
    public void Strip_OscSequenceTerminatedByBel_RemovesSequence()
    {
        // ESC ] 0 ; title BEL — sets terminal title
        AnsiStrip.Strip(Esc + "]0;My Terminal Title" + Bel + "plain").Should().Be("plain");
    }

    [Fact]
    public void Strip_OscSequenceTerminatedBySt_RemovesSequence()
    {
        // ESC ] 0 ; title ST — ST = ESC \
        AnsiStrip.Strip(Esc + "]0;title" + Esc + "\\plain").Should().Be("plain");
    }

    [Fact]
    public void Strip_MultipleOscSequences_RemovesAll()
    {
        const string input = "\e]0;title\atext\e]1;icon\amore";
        AnsiStrip.Strip(input).Should().Be("textmore");
    }

    [Fact]
    public void Strip_OscAndCsiMixed_RemovesBoth()
    {
        const string input = "\e]0;title\a\e[32mGREEN\e[0mtext";
        AnsiStrip.Strip(input).Should().Be("GREENtext");
    }

    [Fact]
    public void Strip_IncompleteEscapeAtEndOfStream_RemovesEsc()
    {
        // Truncated stream ends with bare ESC — the ESC is removed, content preserved
        AnsiStrip.Strip("text" + Esc).Should().Be("text");
    }

    [Fact]
    public void Strip_BareEscInMiddleOfText_RemovesEsc()
    {
        AnsiStrip.Strip("foo" + Esc + "bar").Should().Be("foobar");
    }

    [Fact]
    public void Strip_IncompleteEscapeWithPartialCsi_RemovesEscAndBracket()
    {
        // ESC [ without a final byte — bare ESC stripped, [ left or stripped
        // This tests that a truncated CSI (ESC [ digits...) doesn't crash
        const string input = "\e[123";
        var result = AnsiStrip.Strip("text" + input);
        result.Should().NotContain(Esc);
    }
}
