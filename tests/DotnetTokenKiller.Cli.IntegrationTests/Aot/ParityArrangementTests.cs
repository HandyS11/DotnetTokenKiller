using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>Checks the parity cases' own set-up, which the JIT-versus-AOT comparison cannot notice going wrong.</summary>
public sealed class ParityArrangementTests
{
    /// <summary>
    /// A comparison of two runs that both ignore a seeded file passes while testing nothing. If the seed is not where
    /// dtk reads rtk's config, dtk creates a second file instead of merging into the seeded one.
    /// </summary>
    [Fact]
    public async Task ArrangeRtk_SeedsTheFileDtkMergesItsExclusionIntoAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dtk-parity-arrange-{Guid.NewGuid():N}");
        try
        {
            var sandbox = new ParitySandbox(root);
            ParityCases.Get("init-project").Arrange!(sandbox);
            var seeded = ParityCases.RtkConfigPath(sandbox);
            (await File.ReadAllTextAsync(seeded)).Should().Contain("\"git\"").And.NotContain("\"dotnet\"");

            var output = await ParityProcess.RunAsync(
                ParityRunner.CreateStartInfo(ParityRunner.JitAppHost, sandbox, ["init", "claude", "--dir", "{project}"]), stdin: null);

            output.ExitCode.Should().Be(0, output.Stdout + output.Stderr);
            (await File.ReadAllTextAsync(seeded)).Should().Contain("\"git\"").And.Contain("\"dotnet\"");
            output.Stdout.Should().Contain("rtk's config", "the merge into an existing file reports its note");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
