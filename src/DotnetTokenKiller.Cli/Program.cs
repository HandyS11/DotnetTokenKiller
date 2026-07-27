using System.Text;
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Cli;
using DotnetTokenKiller.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

Console.OutputEncoding = Encoding.UTF8;

// Honour the NO_COLOR convention (https://no-color.org): when the env var is present
// (regardless of value), disable ANSI colors and emoji for all output.
var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

const string dotnetCmd = "dotnet";

// Canonicalize `dotnet <sub>` casing (e.g. `DOTNET BUILD` -> `dotnet build`) so Spectre's
// case-sensitive routing resolves it; unknown/passthrough invocations are left untouched.
args = ArgumentPreprocessor.Normalize(args);

// Passthrough: run any unsupported dotnet subcommand directly, recording what it cost so the
// coverage report can rank which subcommand is worth filtering next.
if (ArgumentPreprocessor.IsPassthrough(args))
{
    return await PassthroughEntryPoint.RunAsync(dotnetCmd, args[1..]).ConfigureAwait(false);
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

    app.Configure(config => CliConfigurator.Configure(config, CliConfigurator.DefaultVersion));

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
