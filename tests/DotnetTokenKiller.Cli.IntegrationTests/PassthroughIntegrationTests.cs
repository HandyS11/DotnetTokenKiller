using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.Passthrough")]
[Trait("Category", "Integration")]
public class PassthroughIntegrationTests
{
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_UnknownSubcommand_ForwardsToDotnet()
    {
        // "dotnet --info" is not a filtered subcommand, so dtk should pass it through
        var (dtkOutput, dtkExit) = await IntegrationTestHelper.RunDtkAsync("dotnet", "--info");
        var (_, dotnetExit) = await IntegrationTestHelper.RunDotnetAsync("--info");

        dtkExit.Should().Be(dotnetExit);
        // The passthrough output should contain key dotnet --info content
        dtkOutput.Should().Contain(".NET SDK");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_UnfilteredSubcommand_ForwardsToDotnet()
    {
        // "dotnet help" is not a registered filtered command
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "help");

        exitCode.Should().Be(0);
        output.Should().NotBeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_TrackingAndTeeBothOff_OpensNeitherTheDatabaseNorALogAsync()
    {
        // The passthrough entry point exists to keep dtk's startup cost near zero for commands it
        // does not filter. With nothing to record and nothing to log it must open neither store —
        // a database or journal file appearing here means that fast path silently regressed.
        var dir = IntegrationTestHelper.NewIsolatedDir();
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "config", "set", "tracking.enabled", "false");
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "config", "set", "tee.mode", "Never");
        DeleteTrackingState(dir); // `config set` itself is a tracked command

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "dotnet", "--version");

        exitCode.Should().Be(0);
        output.Should().NotBeEmpty();
        AssertNothingTracked(dir);
        Directory.Exists(Path.Combine(dir, "tee")).Should().BeFalse();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_TeeOnAndTrackingOff_WritesTheLogWithoutOpeningTheDatabaseAsync()
    {
        // Tee and tracking are independent switches. With only tee on, the log must still be
        // written — and the run must still never be recorded, in the database or in its journal.
        var dir = IntegrationTestHelper.NewIsolatedDir();
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "config", "set", "tee.mode", "Always");
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "config", "set", "tracking.enabled", "false");
        DeleteTrackingState(dir); // `config set` itself is a tracked command

        // A measurable subcommand: interactive ones keep their stdio attached and are never teed.
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "dotnet", "tool", "list");

        AssertNothingTracked(dir);
        Directory.Exists(Path.Combine(dir, "tee")).Should().BeTrue("a tee session was opened for the run");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_MeasurableSubcommand_RecordsAMeasuredRow()
    {
        // An unfiltered run still leaves a trace. `dotnet list reference` is the exemplar: it is the
        // sibling of the filtered `list package`, so it must stay on the passthrough path, and it
        // needs a project — so it fails here, and a failed run must still be recorded and measured.
        var (_, _, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "list", "reference");

        var rows = await ReadCommandRowsAsync(dbPath);

        rows.Should().ContainSingle();
        rows[0].Command.Should().Be("list reference");
        rows[0].Outcome.Should().Be("PassthroughMeasured");
        rows[0].InputTokens.Should().BeGreaterThan(0);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_UnmeasurableSubcommand_RecordsAnUnmeasuredRow()
    {
        var (_, _, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "--version");

        var rows = await ReadCommandRowsAsync(dbPath);

        rows.Should().ContainSingle();
        rows[0].Command.Should().Be("--version");
        rows[0].Outcome.Should().Be("PassthroughUnmeasured");
        rows[0].InputTokens.Should().Be(0);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_InteractivePublish_IsNotFilteredAndRecordsAnUnmeasuredRow()
    {
        // dtk filters `dotnet publish`, but not with --interactive: filtering captures the output and
        // closes stdin, so a credential provider could never prompt. --help keeps the run from building.
        var (output, exitCode, dbPath) =
            await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "publish", "--interactive", "--help");

        exitCode.Should().Be(0);
        output.Should().Contain("--interactive", "the SDK's own publish help is printed, not dtk's");
        var rows = await ReadCommandRowsAsync(dbPath);
        rows.Should().ContainSingle();
        rows[0].Command.Should().Be("publish");
        rows[0].Outcome.Should().Be("PassthroughUnmeasured");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_MeasurableSubcommand_StillPrintsOutput()
    {
        // Streaming must not swallow what the user would otherwise have seen. `list reference` rather
        // than `list package`, which dtk now filters and so no longer reaches the streaming path.
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "list", "reference");

        exitCode.Should().NotBe(0);
        output.Should().NotBeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_UnmeasurableSubcommand_MatchesRawDotnetExitCode()
    {
        var (_, dtkExit) = await IntegrationTestHelper.RunDtkAsync("dotnet", "--version");
        var (_, dotnetExit) = await IntegrationTestHelper.RunDotnetAsync("--version");

        dtkExit.Should().Be(dotnetExit);
    }

    private static async Task<List<(string Command, string Outcome, long InputTokens)>> ReadCommandRowsAsync(
        string dbPath)
    {
        await IntegrationTestHelper.FoldTrackingJournalAsync(dbPath);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT command, outcome, input_tokens FROM commands ORDER BY id";

        var rows = new List<(string, string, long)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return rows;
    }

    /// <summary>Removes what a tracked run leaves in <paramref name="dir"/>: the database and its journal.</summary>
    /// <param name="dir">The isolated directory the runs were pointed at.</param>
    private static void DeleteTrackingState(string dir)
    {
        File.Delete(Path.Combine(dir, "tracking.db"));
        var journal = Path.Combine(dir, "tracking.db.pending");
        if (Directory.Exists(journal))
        {
            Directory.Delete(journal, recursive: true);
        }
    }

    /// <summary>Asserts that nothing was recorded in <paramref name="dir"/>: no database and no journal file.</summary>
    /// <param name="dir">The isolated directory the run was pointed at.</param>
    private static void AssertNothingTracked(string dir)
    {
        File.Exists(Path.Combine(dir, "tracking.db")).Should().BeFalse();
        var journal = Path.Combine(dir, "tracking.db.pending");
        if (Directory.Exists(journal))
        {
            Directory.EnumerateFiles(journal, "*.json").Should().BeEmpty("tracking off must not journal the run");
        }
    }
}
