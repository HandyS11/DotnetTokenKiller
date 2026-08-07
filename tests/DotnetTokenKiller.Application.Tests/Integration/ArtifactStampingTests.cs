using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class ArtifactStampingTests
{
    [Fact]
    public void Apply_HashComment_AppendsStampAsLastLine()
    {
        var stamped = ArtifactStamping.Apply("print('hi')\n", StampStyle.HashComment);

        stamped.Should().StartWith("print('hi')\n");
        stamped.Split('\n')[^2].Should().StartWith("# " + ArtifactStamping.StampPrefix);
        stamped.Should().EndWith("\n");
    }

    [Fact]
    public void Apply_HtmlComment_WrapsStampInAnHtmlComment()
    {
        var stamped = ArtifactStamping.Apply("# Title\n", StampStyle.HtmlComment);

        stamped.Split('\n')[^2].Should().StartWith("<!-- " + ArtifactStamping.StampPrefix)
            .And.EndWith(" -->");
    }

    [Fact]
    public void Apply_BodyWithoutTrailingNewline_AddsOne()
    {
        var stamped = ArtifactStamping.Apply("no newline", StampStyle.HashComment);

        stamped.Should().StartWith("no newline\n");
    }

    [Fact]
    public void IsAuthentic_UnmodifiedHashCommentContent_ReturnsTrue()
    {
        var stamped = ArtifactStamping.Apply("body line one\nbody line two\n", StampStyle.HashComment);

        ArtifactStamping.IsAuthentic(stamped).Should().BeTrue();
    }

    [Fact]
    public void IsAuthentic_UnmodifiedHtmlCommentContent_ReturnsTrue()
    {
        var stamped = ArtifactStamping.Apply("body line one\nbody line two\n", StampStyle.HtmlComment);

        ArtifactStamping.IsAuthentic(stamped).Should().BeTrue();
    }

    [Fact]
    public void IsAuthentic_BodyEdited_ReturnsFalse()
    {
        var stamped = ArtifactStamping.Apply("original\n", StampStyle.HashComment);
        var tampered = stamped.Replace("original", "edited", StringComparison.Ordinal);

        ArtifactStamping.IsAuthentic(tampered).Should().BeFalse();
    }

    [Fact]
    public void IsAuthentic_CrlfCheckout_StillReturnsTrue()
    {
        // A Windows checkout can rewrite LF to CRLF. That is not tampering, so hashing
        // normalizes line endings first; without this the whole feature misfires on Windows.
        var stamped = ArtifactStamping.Apply("line one\nline two\n", StampStyle.HashComment);
        var crlf = stamped.Replace("\n", "\r\n", StringComparison.Ordinal);

        ArtifactStamping.IsAuthentic(crlf).Should().BeTrue();
    }

    [Fact]
    public void IsAuthentic_NoStamp_ReturnsFalse()
    {
        ArtifactStamping.IsAuthentic("just a file\n").Should().BeFalse();
    }

    [Fact]
    public void IsAuthentic_TruncatedHash_ReturnsFalse()
    {
        var stamped = ArtifactStamping.Apply("body\n", StampStyle.HashComment);
        var truncated = stamped[..^10] + "\n";

        ArtifactStamping.IsAuthentic(truncated).Should().BeFalse();
    }

    [Fact]
    public void TryParse_StampedContent_ReturnsBodyWithoutTheStampLine()
    {
        var stamped = ArtifactStamping.Apply("alpha\nbeta\n", StampStyle.HashComment);

        ArtifactStamping.TryParse(stamped, out var body, out var hash).Should().BeTrue();
        body.Should().Be("alpha\nbeta\n");
        hash.Should().Be(ArtifactStamping.ComputeHash("alpha\nbeta\n"));
    }
}
