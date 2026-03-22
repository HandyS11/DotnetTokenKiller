using DotnetTokenKiller.Application;
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

// Known filtered subcommands — keep in sync with the branch registration below
HashSet<string> knownDotnetSubcommands = ["build", "test", "restore", "clean"];

switch (args.Length)
{
    // Passthrough: run any unsupported dotnet subcommand directly without extra DI
    case >= 2 when string.Equals(args[0], "dotnet", StringComparison.OrdinalIgnoreCase) &&
                   !knownDotnetSubcommands.Contains(args[1]):
        var runner = new ProcessCommandRunner();
        return await runner.RunPassthroughAsync("dotnet", args[1..]).ConfigureAwait(false);

    // Auto-insert "--" so dotnet-specific options (e.g. --filter, --no-restore) are
    // forwarded via Spectre's Remaining.Raw without requiring the user to type "--".
    // DTK flags are partitioned out first so they always land before "--" regardless
    // of where the user placed them in the command line.
    case > 2 when string.Equals(args[0], "dotnet", StringComparison.OrdinalIgnoreCase) &&
                  knownDotnetSubcommands.Contains(args[1]) &&
                  !args.Contains("--"):
        HashSet<string> dtkOptions = ["-v", "--verbose", "--show-log"];
        var dtkFlags = new List<string>();
        var dotnetArgs = new List<string>();
        for (var i = 2; i < args.Length; i++)
        {
            if (dtkOptions.Contains(args[i]))
                dtkFlags.Add(args[i]);
            else
                dotnetArgs.Add(args[i]);
        }

        if (dotnetArgs.Count > 0)
        {
            var updated = new List<string>(args.Length + 1) { args[0], args[1] };
            updated.AddRange(dtkFlags);
            updated.Add("--");
            updated.AddRange(dotnetArgs);
            args = [.. updated];
        }

        break;
}

try
{
    var services = new ServiceCollection();
    services.AddInfrastructure();
    services.AddApplication();
    services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);

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
            dotnet.AddCommand<DotnetBuildCommand>("build").WithDescription("Run dotnet build with filtered output");
            dotnet.AddCommand<DotnetTestCommand>("test").WithDescription("Run dotnet test with filtered output");
            dotnet.AddCommand<DotnetRestoreCommand>("restore")
                .WithDescription("Run dotnet restore with filtered output");
            dotnet.AddCommand<DotnetCleanCommand>("clean").WithDescription("Run dotnet clean with filtered output");
        });

        config.AddBranch("integrate", integrate =>
        {
            integrate.SetDescription("Install dtk integration artifacts for an AI assistant provider");
            integrate.AddCommand<ClaudeIntegrateCommand>("claude")
                .WithDescription("Install dtk skill and hook for Claude Code");
            integrate.AddCommand<CopilotIntegrateCommand>("copilot")
                .WithDescription("Install dtk instructions for GitHub Copilot");
        });

        config.AddCommand<GainCommand>("gain").WithDescription("Show token savings analytics");
        config.AddCommand<ResetCommand>("reset").WithDescription("Clear all tracking data");
    });

    return await app.RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
    return 1;
}
