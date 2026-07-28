using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public class PipeIntegrationTests
{
    private const string FailedBuildLog = """
                                          Determining projects to restore...
                                          /src/App.cs(12,20): error CS1002: ; expected [/src/App.csproj]
                                          Build FAILED.
                                              1 Error(s)
                                          """;

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Pipe_Build_CondensesStdinAsync()
    {
        var (output, exitCode, _) =
            await IntegrationTestHelper.RunDtkWithStdinAsync(FailedBuildLog, "pipe", "build", "--exit-code", "1");

        exitCode.Should().Be(1, "the supplied exit code is propagated so CI still fails");
        output.Should().Contain("CS1002");
        output.Should().NotContain("Determining projects to restore", "the filter exists to drop this noise line");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Pipe_DefaultsExitCodeToZeroAsync()
    {
        var (_, exitCode, _) =
            await IntegrationTestHelper.RunDtkWithStdinAsync("Build succeeded.", "pipe", "build");

        exitCode.Should().Be(0);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Pipe_UnknownSubcommand_FailsWithKnownListAsync()
    {
        var (output, exitCode, _) =
            await IntegrationTestHelper.RunDtkWithStdinAsync("anything", "pipe", "publish");

        exitCode.Should().Be(1);
        output.Should().Contain("build").And.Contain("list package");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Pipe_MultiTokenSubcommand_IsAcceptedAsync()
    {
        var (_, exitCode, dbPath) = await IntegrationTestHelper.RunDtkWithStdinAsync(
            "Project 'App' has the following package references", "pipe", "list", "package");

        exitCode.Should().Be(0);
        (await IntegrationTestHelper.ReadTrackedCommandsAsync(dbPath)).Should().Contain("list package");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Pipe_RecordsPipeSourceAsync()
    {
        var (_, _, dbPath) =
            await IntegrationTestHelper.RunDtkWithStdinAsync("Build succeeded.", "pipe", "build");

        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT source FROM commands ORDER BY id";

        var source = (string?)await cmd.ExecuteScalarAsync();
        source.Should().Be("Pipe");
    }
}
