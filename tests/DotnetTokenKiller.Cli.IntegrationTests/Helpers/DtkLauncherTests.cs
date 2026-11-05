using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

public sealed class DtkLauncherTests
{
    private const string DllPath = "/build/dtk.dll";

    private const string AppHostPath = "/build/dtk";

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveAppHost_NoBinary_StartsTheJitApphost(string? testBinary)
    {
        DtkLauncher.ResolveAppHost(testBinary, AppHostPath).Should().Be(AppHostPath);
    }

    [Fact]
    public void ResolveAppHost_Binary_StartsIt()
    {
        DtkLauncher.ResolveAppHost("  /tools/dtk  ", AppHostPath).Should().Be("/tools/dtk");
    }
}
