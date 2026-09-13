using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

public sealed class DtkLauncherTests
{
    private const string DllPath = "/build/dtk.dll";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoBinary_StartsDllThroughDotnet(string? testBinary)
    {
        var (executable, prefix) = DtkLauncher.Resolve(testBinary, DllPath);

        executable.Should().Be("dotnet");
        prefix.Should().Equal(DllPath);
    }

    [Fact]
    public void Resolve_Binary_StartsItDirectly()
    {
        var (executable, prefix) = DtkLauncher.Resolve("  /tools/dtk  ", DllPath);

        executable.Should().Be("/tools/dtk");
        prefix.Should().BeEmpty();
    }
}
