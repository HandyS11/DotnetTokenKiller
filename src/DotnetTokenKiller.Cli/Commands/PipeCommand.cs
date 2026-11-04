using System.Text;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;
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
    /// <summary>
    /// How wide a line of the "Available:" list may be: Spectre's wrap width when the output is not a terminal, and a
    /// narrow terminal's.
    /// </summary>
    private const int AvailableListWidth = 80;

    /// <summary>
    /// The filtered subcommands after "Available: ", wrapped between names so that Spectre never folds a multi-token
    /// name such as "list package" across two lines.
    /// </summary>
    private static readonly IReadOnlyList<string> AvailableLines =
        WrapBetweenNames("Available: ", DotnetSubcommands.Ordered, AvailableListWidth);

    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(
        CommandContext context,
        PipeCommandSettings settings,
        CancellationToken cancellationToken)
        => RunAsync(settings, cancellationToken);

    /// <summary>Runs the command against already-parsed settings, bypassing Spectre's
    /// <see cref="CommandContext"/> plumbing so this is directly callable from in-process tests.</summary>
    /// <param name="settings">The parsed command settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal Task<int> RunAsync(PipeCommandSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!standardInput.IsRedirected)
        {
            // Without this, ReadToEndAsync would block on a terminal until the user pressed Ctrl-D,
            // which reads as a hang rather than as a usage error.
            console.MarkupLine(
                "[red]dtk pipe reads from standard input.[/] Pipe output into it, e.g. [bold]dotnet build | dtk pipe build[/].");
            return Task.FromResult(1);
        }

        if (!DotnetSubcommands.TryMatch(settings.Subcommand, out var match))
        {
            var attempted = string.Join(' ', settings.Subcommand);

            // The list on lines of its own, never after the attempted name: the combined message can exceed the
            // console's default (non-interactive) wrap width, and Spectre would then fold mid-item.
            console.MarkupLine($"[red]No filter for:[/] {attempted.EscapeMarkup()}.");
            foreach (var line in AvailableLines)
            {
                console.MarkupLine(line.EscapeMarkup());
            }

            return Task.FromResult(1);
        }

        if (!string.Equals(match.Name, DotnetSubcommands.Test, StringComparison.Ordinal)
            && FirstTestOption(settings) is { } option)
        {
            console.MarkupLine($"[red]dtk:[/] {option} applies only to dtk pipe test");
            return Task.FromResult(ExitCodes.UsageError);
        }

        // Keyed by the canonical name, the same key FilterKeys aliases, so pipe mode can never
        // address a filter that CLI routing cannot (or vice versa).
        var filter = services.GetRequiredKeyedService<IOutputFilter>(match.Name);

        return pipeFilter
            .RunAsync(filter, match.Name, settings.ExitCode, settings.ToOutputOptions(), settings.ToTestOptions(), cancellationToken);
    }

    /// <summary>
    /// Joins <paramref name="names"/> with ", " after <paramref name="prefix"/>, starting a new line, indented to the
    /// first name, before a name that would pass <paramref name="width"/>.
    /// </summary>
    /// <param name="prefix">The text the first line starts with.</param>
    /// <param name="names">The names to list, in order.</param>
    /// <param name="width">The widest a line may be, unless a single name is wider.</param>
    private static List<string> WrapBetweenNames(string prefix, IReadOnlyList<string> names, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder(prefix);
        var lineHasName = false;
        for (var i = 0; i < names.Count; i++)
        {
            var item = i < names.Count - 1 ? $"{names[i]}," : names[i];
            if (lineHasName && line.Length + 1 + item.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear().Append(' ', prefix.Length);
                lineHasName = false;
            }

            line.Append(lineHasName ? " " : string.Empty).Append(item);
            lineHasName = true;
        }

        lines.Add(line.ToString());
        return lines;
    }

    private static string? FirstTestOption(PipeCommandSettings settings)
    {
        if (settings.Names)
        {
            return "--names";
        }

        if (settings.ShowOutput)
        {
            return "--show-output";
        }

        return settings.FailOnEmpty ? "--fail-on-empty" : null;
    }
}
