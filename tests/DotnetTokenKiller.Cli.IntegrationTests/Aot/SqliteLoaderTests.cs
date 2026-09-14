using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// A tracked run writes its record to the journal and must not load SQLite, on any platform; with tracking off,
/// nothing loads it either. Nothing else notices if a change loses that start-up saving. <c>gain</c>, which
/// folds the journal into the database, is the positive control. dyld reports every image it loads when
/// <c>DYLD_PRINT_LIBRARIES</c> is set, so this runs on macOS, where the AOT binary still loads
/// <c>libe_sqlite3.dylib</c> dynamically; the glibc binaries link SQLite statically and musl has no loader
/// trace. Skipped unless <c>DTK_AOT_BINARY</c> is set; with <c>DTK_AOT_REQUIRED=1</c> a missing binary fails
/// instead.
/// </summary>
public sealed class SqliteLoaderTests
{
    private const string SqliteLibrary = "libe_sqlite3";

    [AotMacOSFact]
    public async Task PipeBuild_NeverLoadsSqlite_GainDoesAsync()
    {
        var binary = AotParitySkip.ReadRequired(AotParitySkip.AotBinaryVariable);
        var root = Path.Combine(Path.GetTempPath(), $"dtk-loader-{Guid.NewGuid():N}");
        try
        {
            var sandbox = new ParitySandbox(root);

            var tracked = await RunPipeBuildAsync(binary, sandbox);
            tracked.Stderr.Should().NotContain(SqliteLibrary,
                "a tracked run writes the journal and must not load SQLite");

            var gain = await RunWithLoaderTraceAsync(binary, sandbox, ["gain", "--json"]);
            gain.ExitCode.Should().Be(0, gain.Stdout + gain.Stderr);
            gain.Stdout.Should().Contain("\"TotalCommands\":1", "gain folds the journal it just found");
            gain.Stderr.Should().Contain(SqliteLibrary,
                "gain reads the database, so dyld must report loading SQLite, or the assertions above prove nothing");

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
        var output = await RunWithLoaderTraceAsync(
            binary,
            sandbox,
            ["pipe", "build", "--exit-code", "1"],
            await File.ReadAllTextAsync(ParityRunner.FixturePath("dotnet_build_errors.txt")));

        output.ExitCode.Should().Be(1, output.Stdout + output.Stderr);
        output.Stdout.Should().StartWith("dotnet build: 3 errors");
        return output;
    }

    private static async Task<ProcessOutput> RunWithLoaderTraceAsync(
        string binary, ParitySandbox sandbox, string[] arguments, string? stdin = null)
    {
        var startInfo = ParityRunner.CreateStartInfo(binary, sandbox, arguments);
        startInfo.Environment["DYLD_PRINT_LIBRARIES"] = "1";
        return await ParityProcess.RunAsync(startInfo, stdin);
    }
}
