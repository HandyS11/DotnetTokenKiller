using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// With tracking off, dtk must not load SQLite: that start-up saving is sub-project 2's, and nothing else
/// notices if a change loses it. dyld reports every image it loads when <c>DYLD_PRINT_LIBRARIES</c> is set, so
/// this runs on macOS, where the AOT binary still loads <c>libe_sqlite3.dylib</c> dynamically; the glibc
/// binaries link SQLite statically and musl has no loader trace. Skipped unless <c>DTK_AOT_BINARY</c> is set;
/// with <c>DTK_AOT_REQUIRED=1</c> a missing binary fails instead.
/// </summary>
public sealed class SqliteLoaderTests
{
    private const string SqliteLibrary = "libe_sqlite3";

    [AotMacOSFact]
    public async Task PipeBuild_TrackingOff_DoesNotLoadSqliteAsync()
    {
        var binary = AotParitySkip.ReadRequired(AotParitySkip.AotBinaryVariable);
        var root = Path.Combine(Path.GetTempPath(), $"dtk-loader-{Guid.NewGuid():N}");
        try
        {
            var sandbox = new ParitySandbox(root);

            var trackingOn = await RunPipeBuildAsync(binary, sandbox);
            trackingOn.Stderr.Should().Contain(SqliteLibrary,
                "with tracking on dyld must report loading SQLite, or the tracking-off assertion below proves nothing");

            var disable = await ParityProcess.RunAsync(
                ParityRunner.CreateStartInfo(binary, sandbox, ["config", "set", "tracking.enabled", "false"]), stdin: null);
            disable.ExitCode.Should().Be(0, disable.Stdout + disable.Stderr);

            var trackingOff = await RunPipeBuildAsync(binary, sandbox);
            trackingOff.Stderr.Should().NotContain(SqliteLibrary, "tracking off must not load SQLite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<ProcessOutput> RunPipeBuildAsync(string binary, ParitySandbox sandbox)
    {
        var startInfo = ParityRunner.CreateStartInfo(binary, sandbox, ["pipe", "build", "--exit-code", "1"]);
        startInfo.Environment["DYLD_PRINT_LIBRARIES"] = "1";

        var output = await ParityProcess.RunAsync(
            startInfo, await File.ReadAllTextAsync(ParityRunner.FixturePath("dotnet_build_errors.txt")));

        output.ExitCode.Should().Be(1, output.Stdout + output.Stderr);
        output.Stdout.Should().StartWith("dotnet build: 3 errors");
        return output;
    }
}
