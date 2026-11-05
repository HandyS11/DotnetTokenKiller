using DotnetTokenKiller.Application.UseCases;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class OutputOptionsTests
{
    [Fact]
    public void OutputOptions_Normalized_KeepsTeePathAndMaxLines_WhenQuiet()
    {
        var normalized = new OutputOptions(1, true, true, "raw.log", 5).Normalized();

        normalized.Should().Be(new OutputOptions(0, false, true, "raw.log", 5));
    }
}
