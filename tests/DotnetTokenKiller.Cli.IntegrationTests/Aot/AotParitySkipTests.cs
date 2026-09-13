using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

public sealed class AotParitySkipTests
{
    private const string Binary = "/tools/dtk";

    [Fact]
    public void Reason_BinarySet_Runs()
    {
        AotParitySkip.Reason(unixOnly: false, Binary, required: null, isWindows: false).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reason_BinaryBlankAndNotRequired_Skips(string? aotBinary)
    {
        AotParitySkip.Reason(unixOnly: false, aotBinary, required: null, isWindows: false)
            .Should().Contain(AotParitySkip.AotBinaryVariable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reason_BinaryBlankAndRequired_Runs(string? aotBinary)
    {
        AotParitySkip.Reason(unixOnly: false, aotBinary, required: "1", isWindows: false).Should().BeNull(
            "a missing binary must fail the run, not skip it, when DTK_AOT_REQUIRED is 1");
    }

    [Fact]
    public void Reason_UnixOnlyOnWindowsWithBinary_Skips()
    {
        AotParitySkip.Reason(unixOnly: true, Binary, required: "1", isWindows: true).Should().NotBeNull();
    }

    [Fact]
    public void Reason_UnixOnlyNotOnWindows_Runs()
    {
        AotParitySkip.Reason(unixOnly: true, Binary, required: null, isWindows: false).Should().BeNull();
    }
}
