using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class KiloPluginTests
{
    [Fact]
    public void Body_IsOpenCodesBodyWithKilosNames()
    {
        KiloPlugin.Body.Should().Be(OpenCodePlugin.Body
            .Replace("`dtk init opencode`", "`dtk init kilo`", StringComparison.Ordinal)
            .Replace("[\"hook\", \"opencode\"]", "[\"hook\", \"kilo\"]", StringComparison.Ordinal)
            .Replace("OpenCode", "Kilo Code", StringComparison.Ordinal));
    }

    [Fact]
    public void Body_SpawnsDtkHookKiloAndExportsANamedPluginFunction()
    {
        KiloPlugin.Body.Should().Contain("[\"hook\", \"kilo\"]").And.Contain("export const DtkPlugin = async");
        KiloPlugin.InvocationSignature.Should().Be("[\"hook\", \"kilo\"]");
    }

    [Fact]
    public void OpenCodeBody_IsBodyForOpenCode()
    {
        OpenCodePlugin.BodyFor("opencode", "OpenCode").Should().Be(OpenCodePlugin.Body);
    }
}
