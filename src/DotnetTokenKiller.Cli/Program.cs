using System.Reflection;
using System.Text;
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Cli;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Infrastructure;
using DotnetTokenKiller.Infrastructure.Execution;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

Console.OutputEncoding = Encoding.UTF8;

// Honour the NO_COLOR convention (https://no-color.org): when the env var is present
// (regardless of value), disable ANSI colors and emoji for all output.
var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

const string dotnetCmd = "dotnet";
const string integrateBranch = "integrate";
const string configBranch = "config";
const string completionCmd = "completion";

// Passthrough: run any unsupported dotnet subcommand directly without extra DI
if (ArgumentPreprocessor.IsPassthrough(args))
{
    var runner = new ProcessCommandRunner();
    return await runner.RunPassthroughAsync(dotnetCmd, args[1..]).ConfigureAwait(false);
}

// Auto-insert "--" so dotnet-specific options (e.g. --filter, --no-restore) are
// forwarded via Spectre's Remaining.Raw without requiring the user to type "--".
// DTK flags are partitioned out first so they always land before "--" regardless
// of where the user placed them in the command line.
args = ArgumentPreprocessor.InsertSeparator(args);

try
{
    var services = new ServiceCollection();
    services.AddInfrastructure();
    services.AddApplication();
    services.AddSingleton<IAnsiConsole>(_ => noColor
        ? AnsiConsole.Create(new AnsiConsoleSettings
        {
            ColorSystem = ColorSystemSupport.NoColors,
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(Console.Out)
        })
        : AnsiConsole.Console);

    var registrar = new DtkTypeRegistrar(services);
    var app = new CommandApp(registrar);

    app.Configure(config =>
    {
        config.SetApplicationName("dtk");
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        config.SetApplicationVersion(version);
        config.Settings.StrictParsing = false;

        config.AddBranch(dotnetCmd, dotnet =>
        {
            dotnet.SetDescription("Run dotnet commands with filtered output");
            dotnet.AddCommand<DotnetBuildCommand>(ArgumentPreprocessor.BuildSubcommand)
                .WithDescription("Run dotnet build with filtered output")
                .WithExample(dotnetCmd, "build", "MyApp.slnx")
                .WithExample(dotnetCmd, "build", "src/MyApp.csproj", "--no-restore");
            dotnet.AddCommand<DotnetTestCommand>(ArgumentPreprocessor.TestSubcommand)
                .WithDescription("Run dotnet test with filtered output")
                .WithExample(dotnetCmd, "test")
                .WithExample(dotnetCmd, "test", "--filter", "Category=Unit");
            dotnet.AddCommand<DotnetRestoreCommand>(ArgumentPreprocessor.RestoreSubcommand)
                .WithDescription("Run dotnet restore with filtered output")
                .WithExample(dotnetCmd, "restore");
            dotnet.AddCommand<DotnetCleanCommand>(ArgumentPreprocessor.CleanSubcommand)
                .WithDescription("Run dotnet clean with filtered output")
                .WithExample(dotnetCmd, "clean");
            dotnet.AddCommand<DotnetFormatCommand>(ArgumentPreprocessor.FormatSubcommand)
                .WithDescription("Run dotnet format with filtered output")
                .WithExample(dotnetCmd, "format")
                .WithExample(dotnetCmd, "format", "--verify-no-changes");
        });

        config.AddBranch(integrateBranch, integrate =>
        {
            integrate.SetDescription("Install dtk integration artifacts for an AI assistant provider");
            integrate.AddCommand<ClaudeIntegrateCommand>("claude")
                .WithDescription("Install dtk skill and hook for Claude Code")
                .WithExample(integrateBranch, "claude")
                .WithExample(integrateBranch, "claude", "--dir", "/path/to/project", "--force");
            integrate.AddCommand<CopilotIntegrateCommand>("copilot")
                .WithDescription("Install dtk instructions for GitHub Copilot")
                .WithExample(integrateBranch, "copilot");
            integrate.AddCommand<GeminiIntegrateCommand>("gemini")
                .WithDescription("Install dtk instructions and hook for Gemini CLI")
                .WithExample(integrateBranch, "gemini");
            integrate.AddCommand<CursorIntegrateCommand>("cursor")
                .WithDescription("Install dtk rules for Cursor")
                .WithExample(integrateBranch, "cursor");
            integrate.AddCommand<WindsurfIntegrateCommand>("windsurf")
                .WithDescription("Install dtk rules for Windsurf")
                .WithExample(integrateBranch, "windsurf");
            integrate.AddCommand<AiderIntegrateCommand>("aider")
                .WithDescription("Install dtk rules for Aider")
                .WithExample(integrateBranch, "aider");
            integrate.AddCommand<JetBrainsAiIntegrateCommand>("jetbrains")
                .WithDescription("Install dtk guidelines for JetBrains AI")
                .WithExample(integrateBranch, "jetbrains");
        });

        config.AddBranch(configBranch, cfg =>
        {
            cfg.SetDescription("View or modify dtk configuration");
            cfg.AddCommand<ConfigShowCommand>("show")
                .WithDescription("Display the current configuration")
                .WithExample(configBranch, "show");
            cfg.AddCommand<ConfigSetCommand>("set")
                .WithDescription("Set a configuration value")
                .WithExample(configBranch, "set", "tracking.enabled", "false")
                .WithExample(configBranch, "set", "display.width", "100")
                .WithExample(configBranch, "set", "tee.mode", "Always");
        });

        config.AddCommand<DoctorCommand>("doctor")
            .WithDescription("Run diagnostics to verify dtk is set up correctly")
            .WithExample("doctor");

        config.AddCommand<CompletionCommand>(completionCmd)
            .WithDescription("Print shell completion script")
            .WithExample(completionCmd, "bash")
            .WithExample(completionCmd, "zsh")
            .WithExample(completionCmd, "fish")
            .WithExample(completionCmd, "powershell");

        config.AddCommand<GainCommand>("gain")
            .WithDescription("Show token savings analytics")
            .WithExample("gain")
            .WithExample("gain", "--days", "7")
            .WithExample("gain", "--project")
            .WithExample("gain", "--json");
        config.AddCommand<ResetCommand>("reset")
            .WithDescription("Clear all tracking data")
            .WithExample("reset");
    });

    return await app.RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    if (noColor)
    {
        await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
    }

    return 1;
}
