using DotnetTokenKiller.Application;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;
using System.Text;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

Console.OutputEncoding = Encoding.UTF8;

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
        dotnet.AddCommand<DotnetRestoreCommand>("restore").WithDescription("Run dotnet restore with filtered output");
        dotnet.AddCommand<DotnetCleanCommand>("clean").WithDescription("Run dotnet clean with filtered output");
    });

    config.AddCommand<GainCommand>("gain").WithDescription("Show token savings analytics");
    config.AddCommand<ResetCommand>("reset").WithDescription("Clear all tracking data");
});

// Passthrough: run any unsupported dotnet subcommand directly without filtering
HashSet<string> knownDotnetSubcommands = ["build", "test", "restore", "clean"];
if (args.Length >= 2 &&
    string.Equals(args[0], "dotnet", StringComparison.OrdinalIgnoreCase) &&
    !knownDotnetSubcommands.Contains(args[1]))
{
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
    await using var sp = services.BuildServiceProvider();
#pragma warning restore CA2007
    var runner = sp.GetRequiredService<ICommandRunner>();
    return await runner.RunPassthroughAsync("dotnet", args[1..]).ConfigureAwait(false);
}

return await app.RunAsync(args).ConfigureAwait(false);
