using System.Reflection;
using DotnetTokenKiller.Cli.Commands;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public class VersionTests
{
    [Fact]
    public void CliAssembly_InformationalVersion_MatchesCsprojVersion()
    {
        var assembly = typeof(DotnetBuildCommand).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        version.Should().NotBeNullOrEmpty()
            .And.MatchRegex(@"^\d+\.\d+\.\d+");
    }
}
