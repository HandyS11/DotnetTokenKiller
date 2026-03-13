using DotnetTokenKiller.Application;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

var services = new ServiceCollection();
services.AddInfrastructure();
services.AddApplication();
services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);

var registrar = new DtkTypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("dtk");
    config.Settings.StrictParsing = false;

    config.AddBranch("dotnet", dotnet =>
    {
        dotnet.SetDescription("Run dotnet commands with filtered output");
        dotnet.SetDefaultCommand<DotnetPassthroughCommand>();
        dotnet.AddCommand<DotnetBuildCommand>("build").WithDescription("Run dotnet build with filtered output");
        dotnet.AddCommand<DotnetTestCommand>("test").WithDescription("Run dotnet test with filtered output");
        dotnet.AddCommand<DotnetRestoreCommand>("restore").WithDescription("Run dotnet restore with filtered output");
        dotnet.AddCommand<DotnetPublishCommand>("publish").WithDescription("Run dotnet publish with filtered output");
        dotnet.AddCommand<DotnetPackCommand>("pack").WithDescription("Run dotnet pack with filtered output");
        dotnet.AddCommand<DotnetCleanCommand>("clean").WithDescription("Run dotnet clean with filtered output");
        dotnet.AddCommand<DotnetRunCommand>("run").WithDescription("Run dotnet run with filtered output");
        dotnet.AddCommand<DotnetEfCommand>("ef").WithDescription("Run dotnet ef with filtered output");
        dotnet.AddCommand<DotnetFormatCommand>("format").WithDescription("Run dotnet format with filtered output");
        dotnet.AddCommand<DotnetNugetCommand>("nuget").WithDescription("Run dotnet nuget with filtered output");
    });

    config.AddCommand<GainCommand>("gain").WithDescription("Show token savings analytics");
});

return await app.RunAsync(args);
