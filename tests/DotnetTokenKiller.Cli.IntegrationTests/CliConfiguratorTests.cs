using FluentAssertions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Locks the CLI's user-facing help surface — command names, descriptions, and examples —
/// against accidental change. These snapshots are the reason a reworded description or a
/// dropped command registration fails the build rather than shipping silently.
/// </summary>
public sealed class CliConfiguratorTests
{
    private const string TestVersion = "1.2.3-test";

    [Fact]
    public Task Configure_RootHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("--help"));
    }

    [Fact]
    public Task Configure_DotnetBranchHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("dotnet", "--help"));
    }

    [Fact]
    public Task Configure_IntegrateHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("integrate", "--help"));
    }

    [Fact]
    public Task Configure_ConfigBranchHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("config", "--help"));
    }

    [Fact]
    public Task Configure_ConfigSetHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("config", "set", "--help"));
    }

    [Fact]
    public Task Configure_CompletionHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("completion", "--help"));
    }

    [Fact]
    public Task Configure_GainHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("gain", "--help"));
    }

    [Fact]
    public Task Configure_DoctorHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("doctor", "--help"));
    }

    [Fact]
    public Task Configure_ResetHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("reset", "--help"));
    }

    [Fact]
    public Task Configure_DotnetBuildHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("dotnet", "build", "--help"));
    }

    [Fact]
    public Task Configure_DotnetTestHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("dotnet", "test", "--help"));
    }

    [Fact]
    public Task Configure_DotnetFormatHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("dotnet", "format", "--help"));
    }

    [Fact]
    public Task Configure_DotnetCleanHelp_MatchesSnapshot()
    {
        // The dotnet branch help truncates its example list at five, so clean's and restore's
        // examples only ever render on their own help pages — snapshot those directly.
        return Verify(RunHelp("dotnet", "clean", "--help"));
    }

    [Fact]
    public Task Configure_DotnetRestoreHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("dotnet", "restore", "--help"));
    }

    [Fact]
    public Task Configure_DotnetListBranchHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("dotnet", "list", "--help"));
    }

    [Fact]
    public Task Configure_DotnetListPackageHelp_MatchesSnapshot()
    {
        // `list package` sits one level deeper than every other subcommand, so neither its examples
        // nor the `list` branch's own description ever render on the dotnet branch page.
        return Verify(RunHelp("dotnet", "list", "package", "--help"));
    }

    [Fact]
    public Task Configure_ConfigShowHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("config", "show", "--help"));
    }

    [Fact]
    public void Configure_Version_ReportsSuppliedVersion()
    {
        RunHelp("--version").Should().Contain(TestVersion);
    }

    [Fact]
    public void Configure_NullConfigurator_Throws()
    {
        var act = () => CliConfigurator.Configure(null!, TestVersion);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void DefaultVersion_IsNeverNullOrEmpty()
    {
        CliConfigurator.DefaultVersion.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Renders a help page through a fixed-width <see cref="TestConsole"/>. The explicit width
    /// keeps Spectre's column wrapping independent of the terminal running the suite, so the
    /// snapshots stay stable across local and CI runs.
    /// </summary>
    /// <param name="args">The command-line arguments to render help for.</param>
    private static string RunHelp(params string[] args)
    {
        var console = new TestConsole().Width(100);
        var app = new CommandApp();
        app.Configure(config =>
        {
            CliConfigurator.Configure(config, TestVersion);
            config.Settings.Console = console;
        });

        app.Run(args);
        return console.Output;
    }
}
