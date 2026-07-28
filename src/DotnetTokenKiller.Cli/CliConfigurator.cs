using System.Reflection;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Domain;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Declares the dtk command tree: command names, descriptions, and usage examples.
/// Extracted from the entry point so the CLI's user-facing help surface is reachable
/// from in-process tests rather than only via an out-of-process binary invocation.
/// </summary>
internal static class CliConfigurator
{
    /// <summary>The <c>dotnet</c> branch name.</summary>
    public const string DotnetCommand = "dotnet";

    /// <summary>The <c>integrate</c> command name.</summary>
    public const string IntegrateCommand = "integrate";

    /// <summary>The <c>config</c> branch name.</summary>
    public const string ConfigBranch = "config";

    /// <summary>The <c>completion</c> command name.</summary>
    public const string CompletionCommand = "completion";

    /// <summary>Version reported by <c>--version</c>, read from the assembly's informational version.</summary>
    public static string DefaultVersion =>
        typeof(CliConfigurator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    /// <summary>Registers every dtk command, branch, description, and example.</summary>
    /// <param name="config">The Spectre.Console.Cli configurator to populate.</param>
    /// <param name="version">The version string reported by <c>--version</c>.</param>
    public static void Configure(IConfigurator config, string version)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.SetApplicationName("dtk");
        config.SetApplicationVersion(version);
        config.Settings.StrictParsing = false;

        config.AddBranch(DotnetCommand, dotnet =>
        {
            dotnet.SetDescription("Run dotnet commands with filtered output");
            dotnet.AddCommand<DotnetBuildCommand>(DotnetSubcommands.Build)
                .WithDescription("Run dotnet build with filtered output")
                .WithExample(DotnetCommand, "build", "MyApp.slnx")
                .WithExample(DotnetCommand, "build", "src/MyApp.csproj", "--no-restore");
            dotnet.AddCommand<DotnetTestCommand>(DotnetSubcommands.Test)
                .WithDescription("Run dotnet test with filtered output")
                .WithExample(DotnetCommand, "test")
                .WithExample(DotnetCommand, "test", "--filter", "Category=Unit");
            dotnet.AddCommand<DotnetRestoreCommand>(DotnetSubcommands.Restore)
                .WithDescription("Run dotnet restore with filtered output")
                .WithExample(DotnetCommand, "restore");
            dotnet.AddCommand<DotnetCleanCommand>(DotnetSubcommands.Clean)
                .WithDescription("Run dotnet clean with filtered output")
                .WithExample(DotnetCommand, "clean");
            dotnet.AddCommand<DotnetFormatCommand>(DotnetSubcommands.Format)
                .WithDescription("Run dotnet format with filtered output")
                .WithExample(DotnetCommand, "format")
                .WithExample(DotnetCommand, "format", "--verify-no-changes");
            dotnet.AddBranch("list", list =>
            {
                list.SetDescription("Run dotnet list commands with filtered output");
                list.AddCommand<DotnetListPackageCommand>("package")
                    .WithDescription("Run dotnet list package with filtered output")
                    .WithExample(DotnetCommand, "list", "package")
                    .WithExample(DotnetCommand, "list", "package", "--outdated");
            });
        });

        config.AddCommand<Commands.IntegrateCommand>(IntegrateCommand)
            .WithDescription("Install dtk integration artifacts for an AI assistant provider")
            .WithExample(IntegrateCommand, "claude")
            .WithExample(IntegrateCommand, "claude", "--dir", "/path/to/project", "--force")
            .WithExample(IntegrateCommand, "copilot")
            .WithExample(IntegrateCommand, "copilot-cli")
            .WithExample(IntegrateCommand, "copilot-cli", "--global")
            .WithExample(IntegrateCommand, "gemini")
            .WithExample(IntegrateCommand, "cursor")
            .WithExample(IntegrateCommand, "windsurf")
            .WithExample(IntegrateCommand, "aider")
            .WithExample(IntegrateCommand, "jetbrains");

        config.AddBranch(ConfigBranch, cfg =>
        {
            cfg.SetDescription("View or modify dtk configuration");
            cfg.AddCommand<ConfigShowCommand>("show")
                .WithDescription("Display the current configuration")
                .WithExample(ConfigBranch, "show");
            cfg.AddCommand<ConfigSetCommand>("set")
                .WithDescription("Set a configuration value")
                .WithExample(ConfigBranch, "set", "tracking.enabled", "false")
                .WithExample(ConfigBranch, "set", "display.emoji", "false")
                .WithExample(ConfigBranch, "set", "tee.mode", "Always");
        });

        config.AddCommand<DoctorCommand>("doctor")
            .WithDescription("Run diagnostics to verify dtk is set up correctly")
            .WithExample("doctor");

        config.AddCommand<Commands.CompletionCommand>(CompletionCommand)
            .WithDescription("Print shell completion script")
            .WithExample(CompletionCommand, "bash")
            .WithExample(CompletionCommand, "zsh")
            .WithExample(CompletionCommand, "fish")
            .WithExample(CompletionCommand, "powershell");

        config.AddCommand<GainCommand>("gain")
            .WithDescription("Show token savings analytics")
            .WithExample("gain")
            .WithExample("gain", "--days", "7")
            .WithExample("gain", "--project")
            .WithExample("gain", "--json");
        config.AddCommand<ResetCommand>("reset")
            .WithDescription("Clear all tracking data")
            .WithExample("reset");
    }
}
