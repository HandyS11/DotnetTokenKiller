using DotnetTokenKiller.Application;
using DotnetTokenKiller.Cli;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Infrastructure;
using DotnetTokenKiller.Infrastructure.Execution;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;
using System.Text;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

Console.OutputEncoding = Encoding.UTF8;

// Honour the NO_COLOR convention (https://no-color.org): when the env var is present
// (regardless of value), disable ANSI colors and emoji for all output.
var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

// Passthrough: run any unsupported dotnet subcommand directly without extra DI
if (ArgumentPreprocessor.IsPassthrough(args))
{
    var runner = new ProcessCommandRunner();
    return await runner.RunPassthroughAsync("dotnet", args[1..]).ConfigureAwait(false);
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
            ColorSystem = (ColorSystemSupport)ColorSystem.NoColors,
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(Console.Out),
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

        config.AddBranch("dotnet", dotnet =>
        {
            dotnet.SetDescription("Run dotnet commands with filtered output");
            dotnet.AddCommand<DotnetBuildCommand>("build")
                .WithDescription("Run dotnet build with filtered output")
                .WithExample("dotnet", "build", "MyApp.slnx")
                .WithExample("dotnet", "build", "src/MyApp.csproj", "--no-restore");
            dotnet.AddCommand<DotnetTestCommand>("test")
                .WithDescription("Run dotnet test with filtered output")
                .WithExample("dotnet", "test")
                .WithExample("dotnet", "test", "--filter", "Category=Unit");
            dotnet.AddCommand<DotnetRestoreCommand>("restore")
                .WithDescription("Run dotnet restore with filtered output")
                .WithExample("dotnet", "restore");
            dotnet.AddCommand<DotnetCleanCommand>("clean")
                .WithDescription("Run dotnet clean with filtered output")
                .WithExample("dotnet", "clean");
        });

        config.AddBranch("integrate", integrate =>
        {
            integrate.SetDescription("Install dtk integration artifacts for an AI assistant provider");
            integrate.AddCommand<ClaudeIntegrateCommand>("claude")
                .WithDescription("Install dtk skill and hook for Claude Code")
                .WithExample("integrate", "claude")
                .WithExample("integrate", "claude", "--dir", "/path/to/project", "--force");
            integrate.AddCommand<CopilotIntegrateCommand>("copilot")
                .WithDescription("Install dtk instructions for GitHub Copilot")
                .WithExample("integrate", "copilot");
            integrate.AddCommand<GeminiIntegrateCommand>("gemini")
                .WithDescription("Install dtk instructions and hook for Gemini CLI")
                .WithExample("integrate", "gemini");
            integrate.AddCommand<CursorIntegrateCommand>("cursor")
                .WithDescription("Install dtk rules for Cursor")
                .WithExample("integrate", "cursor");
            integrate.AddCommand<WindsurfIntegrateCommand>("windsurf")
                .WithDescription("Install dtk rules for Windsurf")
                .WithExample("integrate", "windsurf");
            integrate.AddCommand<AiderIntegrateCommand>("aider")
                .WithDescription("Install dtk rules for Aider")
                .WithExample("integrate", "aider");
            integrate.AddCommand<JetBrainsAiIntegrateCommand>("jetbrains")
                .WithDescription("Install dtk guidelines for JetBrains AI")
                .WithExample("integrate", "jetbrains");
        });

        config.AddBranch("config", cfg =>
        {
            cfg.SetDescription("View or modify dtk configuration");
            cfg.AddCommand<ConfigShowCommand>("show")
                .WithDescription("Display the current configuration")
                .WithExample("config", "show");
            cfg.AddCommand<ConfigSetCommand>("set")
                .WithDescription("Set a configuration value")
                .WithExample("config", "set", "tracking.enabled", "false")
                .WithExample("config", "set", "display.width", "100")
                .WithExample("config", "set", "tee.mode", "Always");
        });

        config.AddCommand<DoctorCommand>("doctor")
            .WithDescription("Run diagnostics to verify dtk is set up correctly")
            .WithExample("doctor");

        config.AddCommand<CompletionCommand>("completion")
            .WithDescription("Print shell completion script")
            .WithExample("completion", "bash")
            .WithExample("completion", "zsh")
            .WithExample("completion", "fish")
            .WithExample("completion", "powershell");

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
        await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
    else
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
    return 1;
}
