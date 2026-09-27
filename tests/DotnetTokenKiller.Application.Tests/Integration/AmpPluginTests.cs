using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class AmpPluginTests
{
    [Fact]
    public void Body_IsByteForByteTheReleasedPlugin()
    {
        // Pins the plugin every Amp user has installed: a byte change would mark every install stale.
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(AmpPlugin.Body)));

        hash.Should().Be("08387ccad2b43bc16fa5bef14b5e0ecece4233daac06e2d38987a89fd095dc9c");
    }

    [Fact]
    public void Body_SpawnsDtkHookAmpAndExportsADefaultPluginFunction()
    {
        AmpPlugin.Body.Should().Contain("amp.on(\"tool.call\"").And.Contain("export default function");
        AmpPlugin.InvocationSignature.Should().Be("[\"hook\", \"amp\"]");
    }
}
