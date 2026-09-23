using System.Text;
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Cli;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

// A harness's pre-tool hook runs this on every shell tool call, so it skips everything below: encoding
// setup, argument normalization, the service container and Spectre.
if (args is [HookCommands.Verb, ..])
{
    return HookEntryPoint.Run(args[1..]);
}

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // No console to configure: a Windows process started without one (a harness's child, a service)
    // has no handle to set it on. Output keeps the default encoding rather than dtk failing to start.
}

// Honour the NO_COLOR convention (https://no-color.org): when the env var is present
// (regardless of value), disable ANSI colors and emoji for all output.
var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

const string dotnetCmd = "dotnet";

// Canonicalize `dotnet <sub>` casing (e.g. `DOTNET BUILD` -> `dotnet build`) so Spectre's
// case-sensitive routing resolves it; unknown/passthrough invocations are left untouched.
args = ArgumentPreprocessor.Normalize(args);

// Only a run that wraps a dotnet child takes over Ctrl+C and SIGTERM: the first one stops the child and
// lets dtk finalize its log and report an exit code, rather than dying with the child still running.
// Every other command keeps the default, dying at once.
using var runCancellation = string.Equals(args.FirstOrDefault(), dotnetCmd, StringComparison.OrdinalIgnoreCase)
    ? RunCancellation.Register()
    : null;
var cancellationToken = runCancellation?.Token ?? CancellationToken.None;

// Passthrough: run any unsupported dotnet subcommand directly, recording what it cost so the
// coverage report can rank which subcommand is worth filtering next.
if (ArgumentPreprocessor.IsPassthrough(args))
{
    return await PassthroughEntryPoint.RunAsync(dotnetCmd, args[1..], runCancellation).ConfigureAwait(false);
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
    services.AddSingleton<IStandardInputState, ConsoleStandardInputState>();
    services.AddSingleton<IWorkingDirectory, ProcessWorkingDirectory>();

    var registrar = new DtkTypeRegistrar(services);
    var app = SpectreCommandApp.Create(registrar);

    app.Configure(config => CliConfigurator.Configure(config, CliConfigurator.DefaultVersion));

    // A cancelled command's OperationCanceledException becomes Spectre's CancellationExitCode, whose
    // default is ExitCodes.Cancelled (130).
    return await app.RunAsync(args, cancellationToken).ConfigureAwait(false);
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
