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
    public async Task Passthrough_MeasurableSubcommand_RecordsAMeasuredRow()
    {
        // An unfiltered run still leaves a trace. `dotnet list reference` is the exemplar: it is the
        // sibling of the filtered `list package`, so it must stay on the passthrough path, and it
        // needs a project — so it fails here, and a failed run must still be recorded and measured.
        var (_, _, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "list", "reference");

        var rows = ReadCommandRows(dbPath);

        rows.Should().ContainSingle();
        rows[0].Command.Should().Be("list reference");
        rows[0].Outcome.Should().Be("PassthroughMeasured");
        rows[0].InputTokens.Should().BeGreaterThan(0);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_UnmeasurableSubcommand_RecordsAnUnmeasuredRow()
    {
        var (_, _, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "--version");

        var rows = ReadCommandRows(dbPath);

        rows.Should().ContainSingle();
        rows[0].Command.Should().Be("--version");
        rows[0].Outcome.Should().Be("PassthroughUnmeasured");
        rows[0].InputTokens.Should().Be(0);
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

    private static List<(string Command, string Outcome, long InputTokens)> ReadCommandRows(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT command, outcome, input_tokens FROM commands ORDER BY id";

        var rows = new List<(string, string, long)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return rows;
    }
}
