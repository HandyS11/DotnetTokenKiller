using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Filters output piped in on stdin from a command dtk did not run.</summary>
/// <param name="pipeFilter">The pipe filter use case.</param>
/// <param name="services">The service provider, used to resolve the filter keyed by subcommand.</param>
/// <param name="console">The Spectre.Console sink for error messages.</param>
/// <param name="standardInput">Reports whether stdin is redirected.</param>
internal sealed class PipeCommand(
    PipeFilterUseCase pipeFilter,
    IServiceProvider services,
    IAnsiConsole console,
    IStandardInputState standardInput) : AsyncCommand<PipeCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        PipeCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!standardInput.IsRedirected)
        {
            // Without this, ReadToEndAsync would block on a terminal until the user pressed Ctrl-D,
            // which reads as a hang rather than as a usage error.
            console.MarkupLine(
                "[red]dtk pipe reads from standard input.[/] Pipe output into it, e.g. [bold]dotnet build | dtk pipe build[/].");
            return 1;
        }

        if (!DotnetSubcommands.TryMatch(settings.Subcommand, out var match))
        {
            var attempted = string.Join(' ', settings.Subcommand);
            var known = string.Join(", ", DotnetSubcommands.Ordered);

            // Two lines rather than one: the combined message can exceed the console's default
            // (non-interactive) wrap width, and Spectre would then fold mid-item, splitting a
            // multi-token name like "list package" across lines.
            console.MarkupLine($"[red]No filter for:[/] {attempted.EscapeMarkup()}.");
            console.MarkupLine($"Available: {known.EscapeMarkup()}");
            return 1;
        }

        // Keyed by the canonical name, the same key FilterKeys aliases, so pipe mode can never
        // address a filter that CLI routing cannot (or vice versa).
        var filter = services.GetRequiredKeyedService<IOutputFilter>(match.Name);

        return await pipeFilter
            .RunAsync(filter, match.Name, settings.ExitCode, settings.ToOutputOptions(), cancellationToken)
            .ConfigureAwait(false);
    }
}
