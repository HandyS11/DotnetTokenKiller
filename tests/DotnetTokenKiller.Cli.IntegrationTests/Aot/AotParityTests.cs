using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// Runs every command family through the JIT build and through the binary named by
/// <c>DTK_AOT_BINARY</c>, and requires the same exit codes, output, written files and tracking rows.
/// Spectre.Console.Cli does not support Native AOT; this is what makes dtk's use of it tested rather
/// than hoped for. Skipped unless <c>DTK_AOT_BINARY</c> is set; with <c>DTK_AOT_REQUIRED=1</c> a missing
/// binary fails instead.
/// </summary>
public sealed class AotParityTests
{
    [AotParityTheory]
    [MemberData(nameof(ParityCases.PortableNames), MemberType = typeof(ParityCases))]
    public Task Portable_MatchesJitBuildAsync(string caseName) => AssertParityAsync(caseName);

    [AotParityUnixTheory]
    [MemberData(nameof(ParityCases.UnixOnlyNames), MemberType = typeof(ParityCases))]
    public Task UnixOnly_MatchesJitBuildAsync(string caseName) => AssertParityAsync(caseName);

    private static async Task AssertParityAsync(string caseName)
    {
        var (jit, aot) = await ParityRunner.RunBothAsync(ParityCases.Get(caseName));

        aot.Steps.Should().Equal(jit.Steps, "every step's exit code and output must match the JIT build");
        aot.Files.Should().BeEquivalentTo(jit.Files, "the same files, with the same content, must be written");
        aot.TrackingRows.Should().Equal(jit.TrackingRows, "token counts and outcomes must be recorded identically");
    }
}
