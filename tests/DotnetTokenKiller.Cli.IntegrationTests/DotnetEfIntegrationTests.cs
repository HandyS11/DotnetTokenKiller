using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetEfIntegrationTests
{
    private static readonly string SampleEfCore =
        IntegrationTestHelper.SamplePath("SampleApp.EfCore");

    [SkippableFact]
    public async Task EfMigrationsList_SampleEfCore_OutputIsCompact()
    {
        Skip.If(!IsEfToolAvailable(), "dotnet-ef tool not found — skipping EF integration tests");

        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "ef", "migrations", "list", "--project", SampleEfCore);
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "ef", "migrations", "list",
            "--", "--project", SampleEfCore);

        exitCode.Should().Be(0);
        output.Trim().Should().Be("1 migration (latest: 20260314161654_InitialCreate)");

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(70.0,
            "ef migrations list should achieve ≥70% token savings");
    }

    [SkippableFact]
    public async Task EfMigrationsList_SampleEfCore_NoBannerOrBuildPreamble()
    {
        Skip.If(!IsEfToolAvailable(), "dotnet-ef tool not found — skipping EF integration tests");

        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "ef", "migrations", "list",
            "--", "--project", SampleEfCore);

        output.Should().NotContain("Build started");
        output.Should().NotContain("Build succeeded");
        output.Should().NotContain("_/\\__");
        output.Should().NotContain("Entity Framework Core");
    }

    [SkippableFact]
    public async Task EfDatabaseUpdate_TempSqlite_Success_OutputStartsWithCheckmark()
    {
        Skip.If(!IsEfToolAvailable(), "dotnet-ef tool not found — skipping EF integration tests");

        var tempDb = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        try
        {
            var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
                "dotnet", "ef", "database", "update",
                "--", "--project", SampleEfCore, "--connection", $"Data Source={tempDb}");

            exitCode.Should().Be(0);
            output.Should().StartWith("✓ database updated (1 migration applied)");
        }
        finally
        {
            if (File.Exists(tempDb))
                File.Delete(tempDb);
        }
    }

    [SkippableFact]
    public async Task EfMigrationsAdd_NoModelChanges_Failure_ExitCodeNonZero()
    {
        Skip.If(!IsEfToolAvailable(), "dotnet-ef tool not found — skipping EF integration tests");

        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "ef", "migrations", "add", "InitialCreate",
            "--", "--project", SampleEfCore);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("✓ database updated (already up-to-date)");
    }

    [SkippableFact]
    public async Task EfDatabaseUpdate_InvalidPath_Failure_ExitCodeNonZero()
    {
        Skip.If(!IsEfToolAvailable(), "dotnet-ef tool not found — skipping EF integration tests");

        const string invalidDbPath = "/tmp/nonexistent-ef-dir-xyz123/test.db";
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "ef", "database", "update",
            "--", "--project", SampleEfCore, "--connection", $"Data Source={invalidDbPath}");

        exitCode.Should().NotBe(0);
        output.Should().StartWith("✓ database updated (already up-to-date)");
    }

    private static bool IsEfToolAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("ef");
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
